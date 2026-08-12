# Engineering Quality Review

Status: living independent quality audit; active findings require disposition<br>
Date: 2026-08-11<br>
Last reassessed: 2026-08-11 against integrated `main` `5b8556a`; prior retained
evidence remains scoped to the commits named in each finding<br>
Scope: architecture, maintainability, correctness, security, performance,
verification credibility, UI/UX foundations, and product readiness

## Executive assessment

The quality trajectory is **improving, but the repository is not yet at the
standard of a cohesive senior platform team**.

### Current review delta — compact tray reachability accepted

DLV-105 `42bcf9c`, integrated through `d23db8b`, replaces silent catalog
truncation with one `TrayLayout`-owned visible window and explicit previous/next
overflow controls. Paint, pointer targeting, and host accessibility consume the
same bounds; normal Left/Right retains stable full-catalog order, while pointer
or UIA overflow selects the exact adjacent hidden stable ID without entering
widget content. Visible ListItems publish full-set position/size and overflow
Buttons announce direction plus hidden count. The diff does not alter managed
catalog/persistence, public widget APIs, preferred extents, or compositor
ownership. Retained focused Release evidence covers exact-fit, N+1, large
catalogs, first/middle/last selection, compact scaling, pointer, keyboard,
controller, and native UIA projection. User confirmation in the freshly
launched compact and wide overlay remains the final visual check. The clean
main native-only Release refresh passed and exact planner tip `c836960` is
visibly running as responsive PID 9228.

### Current review delta — complete-surface compositor rejected live

DLV-025 `16f9f47`, integrated through `5b8556a`, proved that one cohesive
Windows-10-compatible DirectComposition owner can render and commit a complete
destination before final HWND geometry. It did not prove a correct packaged
transparency or motion result. The user's recording from exact accepted-main
PID 25164 shows the unused client area as an opaque near-black rectangle around
the overlay and shows visibly poor different-size widget transitions.

The implementation clears the premultiplied-alpha composition surface to
opaque RGB(1,2,3) while retaining `WS_EX_LAYERED` color-key/global-alpha state
on the HWND. Automated fixtures established update ordering but never
established that color-key transparency applies to composed visual content.
Session diagnostics also expose a cadence problem: equivalent size changes run
several waited composition/geometry commits over about 100-150 ms, and populated
Game Launcher frames take roughly 49-63 ms to draw. The recording timestamp has
no typed transition event, so exact frame correlation remains an observability
gap rather than an inferred error code. Provisional acceptance is revoked;
GBA-004 is Open and DLV-107 follows already-active DLV-106 at its next clean
boundary. The correction must own one alpha model and one nonblocking
composition motion policy, retaining the good complete-destination ordering
without per-widget masks, delayed concealment, per-tick blocking HWND resize,
or a second permanent renderer.

### Current review delta — Game Launcher `TextEntry` regression

The current production session for PID 35472 contains five identical Game
Launcher failures between 16:53:06 and 16:55:11:
`Unsupported view node kind 'TextEntry'`. The 1,224-line session contains no
other failure/error/exception/timeout/malformed/denial/crash/protocol/warning/
rejection/unavailable signature. Historical August 10 Spotify/YT Music worker-
start and Settings invalid-payload entries did not recur and are not reopened.

This was a shared bridge regression, not a Game Launcher implementation defect.
Protocol v15 and the SDK validate and emit `TextEntry`; Network Controls also
emits it for protected Wi-Fi; the native bridge parser and host-owned modal
already support it. `BridgeRenderStyles.RoleFor` omits the node and throws while
building computed styles. Tests prove the components on either side but no
production-shaped case sends the existing node through bridge style resolution.
Accepted DLV-100 `760a9bb`, integrated through `54fbd12`, adds exactly one
canonical `textEntry` role and exact installed Game Launcher/Network Controls
route without expanding into protocol or native-modal redesign. Unknown kinds
remain fail-closed. Focused evidence passes Bridge 77/77, Game Launcher 45/45,
Network Controls 24/24, the installed route, and 55 docs. The coherent main
Release was fully repackaged and launched as PID 26556 for live confirmation.

### Current review delta — full-application widget resource boundary

The user has explicitly rejected a product model in which installed widgets are
required to remain small. A widget may be a full application with application-
scale private state, databases, indexes, caches, computation, navigation, and
an owned helper process tree. Accepted DLV-101 `f27f4d7`, integrated through
`9c5de8e`, removes the prototype's 16-256 MiB worker memory range and one-process
Job ceiling. Pre-release compatibility was not used to retain them.

This does not make the shared native host unbounded. IPC frames, validated
current render trees, recursion and strings, pending actions, publication
admission, native/GPU objects, host caches, capability requests, package
ingestion, and concurrent host-owned sessions remain explicitly limited. A
large application keeps its complete model privately and presents a paged or
virtualized current route; the host rejects unsafe submissions before native
allocation, retains the last valid view when possible, and returns a precise
diagnostic rather than truncating content. Job accounting, non-breakaway
process-tree ownership, kill-on-close, integrity, UI restrictions, and bounded
teardown also remain containment requirements, not product-size quotas.

The accepted implementation preserves pre-resume Job assignment and complete
process-tree accounting while making manifest memory guidance optional advisory
metadata. Its installed AppContainer fixture proves private data, an owned
helper, and kill-on-close; a separate oversized-presentation fixture proves the
shared host still fails closed without harming a neighboring widget. Focused
Release evidence passes Runtime 74/74, Bridge 78/78, SDK 87/87, worker host
10/10, and Catalog 35/35.

### Current review delta — dependable Media Sessions recovery

Accepted DLV-103 `f87631c`, integrated through `9c5de8e`, replaces the Now
Playing widget's detached reload ownership with one SDK-owned Active
SingleFlight generation. Subscription admission precedes the current snapshot,
zero sessions is a successful empty state, Retry performs one real current-
generation attempt, last-good sessions survive transient refresh/channel
failure, and cancellation-ignoring stale completions cannot publish.

The broker records only bounded transition-deduplicated Media Sessions stage and
safe error code; player/session identity, metadata, provider bodies, process
details, and credentials remain excluded. Retained focused Release evidence
passes SDK 87/87, PlatformBroker 55/55, Windows Media 13/13, Now Playing 20/20,
WidgetBridge 79/79, documentation 57/57, and the installed AppContainer
lifecycle/retry route. The fully packaged Release is visibly running as PID
32140 for live recurrence testing.

### Current review delta — visible layout and focus corrections

Accepted DLV-104 `99e4932`, integrated through `1ef4666`, gives Game Launcher
one explicit responsive vertical ownership model: fixed header/source/query/
footer regions and one bounded collection viewport with stable anchor/focus
identity. Compact heading/source branches do not duplicate application state.
Focused Game Launcher 46/46, renderer 4,777, shared geometry 589, docs 58/58,
and package validation pass. Final clipping closure remains the user's live
packaged check; malformed capture output is not an acceptance input.

Accepted DLV-106 `f119a1f`, integrated through `dde4981`, also closes the
outgoing-focus authority gap during cold tray switches. Retained pixels may
remain, but committed widget focus is cleared and only current destination tray
semantics are published until explicit entry. The production-host Audio Mixer
to Game Launcher fixture proves input owner, visual focus, semantic focus, and
UIA remain tray-owned across retained and admitted frames. DLV-107 is already
active on the same platform lane and owns the separate black-perimeter/motion
regression; DLV-106 did not broaden into compositor redesign.

### Current review delta — trusted artwork failure presentation

The packaged DLV-100 session corrected TextEntry admission but exposed a
separate shared native presentation defect. PID 26556 logged 45 Game Launcher
`image_failed` lines for 15 stable node IDs in five seconds; all were the same
terminal `Trusted artwork is unavailable` result repeated across three paints.
The lazy provider contract legitimately cannot know that artwork exists until
the visible host requests it, so removing handles through eager catalog probes
would regress the accepted demand-only architecture.

DLV-102 therefore belongs to the native cache/renderer boundary after DLV-025:
terminal-unavailable trusted artwork must render the existing shared tile glyph
and diagnose only the failure-state transition, while pending, late success,
new revision recovery, stale rejection, focus/UIA, and cache bounds remain
intact. This is not authority for a widget-specific placeholder, suppressed
global diagnostics, provider redesign, or unbounded failed-handle history.

### Current review delta — protected Wi-Fi candidate

DLV-087 candidate `8c2949c` plus native-link correction `376e67c` is not
accepted. Its product boundary is directionally strong: the Network Controls
worker receives only a text-entry action and closed result, the trusted native
host owns the masked prompt, current widget/action/scope/generation authority is
revalidated after the nested loop, the provider uses documented per-user Native
Wi-Fi APIs with overwrite disabled, and focused suites are green. The single
exact-commit aggregate stopped once on missing `oleaut32` linkage in the
canonical native path; the append-only correction closes that build parity and
the aggregate was not repeated.

The credential lifetime does not meet the assignment. Native code first puts
the password in WinRT JSON values and ordinary UTF-16/UTF-8 strings. The bridge
then retains the request in an uncleared managed byte array and `JsonElement`;
classification and handling deserialize the full request twice, producing two
immutable password strings. Clearing downstream `char[]` and XML buffers does
not erase those copies, and the password EDIT control is destroyed without
overwriting its text. This is a boundary-design failure, not a request for
general transport hardening.

Rollback ownership is also too weak. The candidate names the temporary profile
from the SSID and later deletes by interface plus that common profile name. It
refuses a profile already present during `WlanSetProfile`, but an independent
actor can replace the profile before asynchronous failure, timeout, or disposal;
the candidate would then delete state it no longer owns. DLV-093 is queued after
already-active visible DLV-091 to add one zeroed mutable secret path and an
attempt-unique, conditionally verified rollback identity. DLV-087 remains
unintegrated until that correction passes.

### Current review delta — installed Game Launcher product slice

DLV-082 `a862ed4`, integrated through `ef8fbab`, adds the first explicit
multi-source partial-health surface without expanding adapter or launch
authority. One provider-owned immutable observation maps at most 16 sanitized
source rows through broker and SDK validation; Game Launcher renders non-color
Healthy, Degraded, Unavailable, and Refreshing text while retaining healthy and
last-good launchable rows. The SDK API addition is reviewed and additive, source
IDs are hashed observation tokens, and widgets cannot select or refresh an
adapter. Focused evidence passes SDK 87/87, compatibility 12/12, broker 52/52,
provider 46/46, Game Launcher 37/37, installed AppContainer 6/6, and 55 docs.
The one integrated aggregate stopped after 39 seconds on pre-existing YT Music
metadata drift: `YtmDesktopApiClient.PackageVersion` is `0.2.6` while the
current immutable package manifest is `0.2.7`. The changed SDK checks had
passed; the aggregate was not rerun. Accepted DLV-086 `abbf54b`, integrated
through `14f7451`, aligns the source constant, pairing `appVersion`, README
commands, and manifest-equality guard at `0.2.7`; YT Music passes 55/55 and no
immutable package bytes were republished. Full Release packaging for the prior
visible prefix succeeded and accepted main is visibly running as PID 20024.

Independent review of DLV-085 candidate `e0edfb8` accepted the management
boundary but initially rejected the complete identity behavior. Settings receives only a
sanitized existence/token projection; the bridge registry remains the singular
worker-generation owner; and the private-state backend remains the document
owner. Focused evidence passes Runtime 74/74, private state 10/10, Settings
54/54, diagnostics 16/16, catalog 35/35, Bridge 73/73, and the installed
two-widget clear fixture. The candidate nevertheless duplicates production
installed-instance derivation for disabled packages as `<widget-id>.disabled`
instead of `BridgeCatalog`'s version-derived `installed.<hash>`. That is a
correctness and maintainability failure: one logical authority had two identity
owners, and enabled-to-disabled state could survive a reset unnoticed. Accepted
DLV-089 `022ffc4` now gives production and disabled management paths one
`InstalledWidgetInstanceIdentity` owner. Bridge 73/73 proves stale-token refusal,
exact clear without worker creation, neighbor isolation, and clean re-enable.
The coherent Settings correction is integrated through `dc1bc16`; the canonical
aggregate was not rerun.

DLV-088 candidate `8295983` also demonstrated why direct managed tests are not
sufficient acceptance for this product seam. Its final Game Launcher suite
passes 43/43 with coherent Restore/Back readiness, focus, and late-generation
rejection. The only installed generic-worker run
`20260811T174504Z-9a7a377a` still returned every game as disabled
`Unavailable`, exactly matching the DLV-084 rejection; it ran before the final
route-token correction and was honestly retained rather than repeated. Because
that later edit initially had no production-shaped evidence, the candidate was
not accepted from direct tests alone. DLV-090 then ran the final exact prefix
once: `20260811T175350Z-011a57cd` passes the generic AppContainer group 6/6 with
the exact restored enabled row and no Refresh. The coherent Game Launcher
correction is integrated through `dc1bc16` without expanding into provider,
SDK, or native work.

DLV-078 `6d30f5e`, integrated through `a072d6f`, closes the independently
reproduced cold-worker presentation-ordering defect without pretending to solve
DLV-025's compositor problem. A widget-to-widget transition now revokes old
lifecycle, input, focus, and actionable UI Automation authority, commits the
prior admitted snapshot once as inert visual-only content, and starts the cold
destination from the already-posted refresh. Only the destination's first
admitted snapshot replaces that retained content. The correction adds no public
contract, per-widget branch, timer, frame loop, or extra bridge request. Its
isolated production-host fixture uses authenticated readiness plus renderer
identity/sequence diagnostics rather than screenshot pixels and covers delayed
Spotify/Games starts, reversal, and same-identity restart. Focused results pass
the exact host fixture, 54 targeting checks, 56 transition checks, and the
Release host build. Live packaged cycling remains the acceptance boundary; the
real HWND resize/dark-band issue is still blocked at DLV-025's atomic-compositor
decision.

### Planner decision evidence — atomic presentation and trusted video

Official Windows composition contracts narrow DLV-025's credible next step.
`IDCompositionSurface` is available to desktop apps from Windows 8 and applies
new pixels only after `BeginDraw`, complete coverage of the update rectangle,
`EndDraw`, and a device `Commit`; this supplies a bounded offscreen-content
prototype compatible with the product's current Windows 10 floor. The newer
[composition swapchain](https://learn.microsoft.com/en-us/windows/win32/comp_swapchain/comp-swapchain)
does provide atomic buffer/property presents and documented atomic resize, but
requires Windows 11 build 22000.194 plus WDDM 2.0 and explicitly requires a
separate older-system path. It would therefore create two presentation owners
or raise the product floor before it proves this defect. The recommended user-
authorized DLV-025 gate is one DirectComposition-surface prototype that renders
the complete destination offscreen, retains the prior committed surface until
the destination is ready, and measures whether visual-content commit plus the
existing HWND geometry can avoid every exposed dark interval. Do not authorize
the Windows-11-only composition-swapchain path unless that smaller gate proves
the HWND boundary itself still requires it.

Official WebView2 guidance does not invalidate DLV-062's measured resource
stop. WebView2 intentionally uses a multi-process Edge architecture. Its
supported `TrySuspend` path requires the controller to be invisible and is best
effort; `MemoryUsageTargetLevel.Low` is likewise recommended for inactive
views. Those controls can improve hidden/unpinned cleanup, but cannot reduce the
348.7 MiB visible pinned-player cost while preserving visible playback. Sharing
one environment mainly benefits multiple controls, while DLV-062 already gates
one surface. Under the current 128 MiB product limit, another generic suspension
pass is not a credible visible-player optimization milestone. Keep YouTube v1
blocked unless the user explicitly raises the budget or authorizes a bounded
content/process-specific experiment with a hard stop and no account work.

DLV-060 `8ca0859`, DLV-066 `8a5ec7f`, public-contract correction DLV-074
`d0a1014`, and launch-lifecycle milestone DLV-067 `ddf7626` are accepted and
integrated through `3d8f486`. The product now has a
bundled installed-only Game Launcher over the same normalized provider, opaque
cursor query, lazy artwork, and exact SavedId launch authority as Games & Apps.
The production widget remains a roughly 600-line lifecycle/action/state owner
over pure presentation and value-based organization policy rather than another
application-sized partial type. Its collection/organization state is bounded to
96 display rows, 32 organized identities, 16 explicit groups, and four variants
per group; launch results retain at most 32 SavedIds. Missing identities remain
visible but non-authorizing until exact revalidation. The internal observation
route carries only typed state/support flags: current Windows and Steam adapters
claim `Launcher started`, acknowledgement-only adapters remain `Request
accepted`, and no PID, HWND, path, command, or store identity crosses the widget
boundary. Retained focused evidence passes SDK 86/86, compatibility 12/12,
provider 45/45, broker 52/52, launcher 20/20, installed generic-worker 6/6, and
55 documentation contracts. The complete managed/runtime graph was republished
from accepted main and the visible Release is running as PID 33360.

The full main build later failed its unchanged `WidgetSwitchHostTests` with the
more precise assertion that worker startup replaced the prior admitted content
with a transient surface. All changed managed outputs had already published,
and the failure is outside the accepted Game Launcher diff; it was not retried
unchanged or misrepresented as a launcher regression. Queued DLV-078 separates
retained-content/first-snapshot ordering from DLV-025's blocked animated resize/
compositor decision. DLV-075 remains Assigned; after it releases the shared
native boundary, platform DLV-078 can run beside widgets DLV-076 before DLV-077.

### Prior review delta — normalized game-library ownership

Independent review rejects DLV-094 candidate `3fdbc19` pending DLV-096. The
candidate's decode and payload reads are demand-driven, bounded, and isolated on
a separate four-operation lane, but `WindowsSteamApplicationSource` still calls
`WindowsSteamArtworkSource.Discover` while enumerating every manifest. That
method opens the trusted root, cache directory, and candidate artwork file and
captures file metadata, so ordinary catalog refresh still depends on artwork
filesystem latency. The new artwork-operation lane is also absent from
`WindowsAppLibraryProvider.CompleteDisposalAsync`: terminal cleanup cancels the
lifetime and joins only `_scanGate`, then disposes sources and clears state even
if a cancellation-ignoring artwork resolver still owns admitted source work.
This is a real ownership regression, not a request for broader provider
refactoring. DLV-096 is queued after already-active DLV-095 to make discovery
entirely demand-only and add one bounded shared terminal result that joins both
scan and artwork operations without racing source disposal.

Independent review also rejects DLV-095 candidate `bd270c9` pending DLV-097,
without interrupting already-active DLV-096. The new read grant and widget flows
preserve opaque normalized authority and focused tests pass, but the public SDK
confirmation path validates only SavedId equality before returning the remaining
untrusted item fields. A malformed AppId, undefined kind, or invalid display/
source value could therefore enter widget state instead of failing closed. The
native observer's `MaximumWindows` counter also advances only when an eligible
process identity is appended; arbitrarily many excluded or unreadable windows
can be examined first. DLV-097 reuses the existing validation contract, counts
every enumerated top-level window before filtering, and adds only the boundary
fixtures needed to close those two gaps.

Independent review rejects DLV-096 candidate `8291c53` pending DLV-098, without
interrupting active DLV-097. The lazy registration no longer opens artwork-cache
files during catalog enumeration, and cancellation-ignoring artwork work now
produces a bounded shared terminal failure. However, after acquiring artwork
capacity, `CompleteDisposalAsync` starts source disposal tasks before it acquires
the scan and observation gates. A later drain timeout can therefore coexist with
disposed sources and cleared state while admitted work still runs; inherited
tests currently assert this unsafe order. The new roots/generation/observed-
revision locator dictionaries also have no prune or capacity policy across
catalog generations. DLV-098 makes complete multi-lane drain a precondition for
all teardown and binds retained locator generation state to current catalog
ownership without reviving stale handles.

DLV-097 candidate `46d1938` is accepted: inspection is bounded before native
eligibility/process work, the SDK shares one closed app-item validator, and
malformed confirmation cannot mutate either widget's private state. DLV-098
candidate `48d19f1` closes multi-lane terminal ordering and bounds the current
locator map; accepted correction DLV-099 `fb7fa34` stages candidate locators
without mutating live state and promotes/retires them only inside the matching
latest source-generation commit. Forced cancellation and out-of-order fixtures
prove losing candidates cannot invalidate still-current artwork. The complete
DLV-094/095/096/097/098/099 prefix is accepted and integrated through
`c6d76a3`; focused final evidence is provider 62/62 and docs 55/55.

DLV-059 `c7c354d`, corrected by DLV-071 `355a858`, is accepted and integrated
through `6f4c642`. The provider's central Start Menu/AppsFolder/Steam discovery,
resolve, launch, and artwork switches become private normalized source owners,
raw launch identity remains inside those owners, and the production provider is
now their single exact-once terminal owner. Composite shutdown reaches bounded
provider cancellation/drain, late completion cannot republish state or
authority, one source failure does not skip the others, and post-terminal calls
fail before Shell/source work. Focused provider evidence passes 41/41 and 54
documentation contracts pass.

DLV-070 `c61a49d`, integrated through `0b21384`, also closes the duplicate-host
ownership defect before later pinning work adds more persistent windows. One
per-user/profile owner is elected before platform initialization; authenticated
bounded Show clients exit without creating a second bridge, catalog, controller
lease, or HWND. Exact visible integration retained one production PID across
two launches.

DLV-060 originally stopped correctly before edits after proving a framework gap: the
public app-library service and broker materialized and capped one snapshot at
512 items. Accepted DLV-072 `fe66470`, integrated through `7f23738`, replaces
that obsolete pre-release path with bounded revision-bound cursor queries,
migrates Games & Apps, and keeps the complete 10,000-item catalog solely in the
normalized provider. Focused provider/broker/SDK/Games/bridge/generic-worker
evidence passes. Its one clean exact-commit aggregate was run once and stopped
on inherited YT Music package metadata drift after the changed SDK checks passed;
the accepted Game Launcher slice above now consumes that cursor contract.

This cycle closes the bounded security-stabilization implementation gate and
then returns to product work. DLV-001 (`d0c0420`) completes the exact-token,
host-owned authority-recovery operator surface; its later exact-commit aggregate
was interrupted and remains checkpoint debt rather than a reason to reopen the
subsystem. Installed-widget security is frozen unless a reproducible P0,
demonstrated threat-model violation, or planned-release blocker is assigned.

Product trajectory is now **improving**. DLV-002 (`1738618`) adds schema-v2
Games & Apps state with explicit automatic provenance and durable exclusions,
walks the bounded trusted catalog, preserves user order and focus, retains
missing identities for stable reappearance, treats replacement identities as
new games, and keeps Application/Unknown entries opt-in. Its focused and
minimal real-package evidence is green without paying the repository-wide test
cost. Shared Button geometry, Spotify's coherent presentation boundary and
responsibility split, and deterministic tray hold-Y refresh are now accepted.
The Games & Apps surface and non-authorizing restart warm start, second advanced-
widget lifecycle migration, production-host action-failure composition, and a
real Games & Apps responsibility split are accepted. Trusted artwork, shared
list/focus and geometry corrections, Audio
endpoint blocker, and physical/product evidence remain open.

The 2026-08-10 visible relaunch exposed a release-composition gap that focused
tests did not catch: stale Community packages failed before connecting and YT
Music replaced the primary failure with a secondary hidden-cache error. DLV-052
is now accepted through `56f6908`. Generic-loader failures use a closed safe
code, lifecycle establishment and first-snapshot admission commit together, and
Retry owns one fresh generation. Review of the real profile caught a second gap
that the empty-catalog fixture missed: install succeeded while the older version
remained selected. The corrected workflow and fixture now prove disable,
install, explicit select, enable, and content-digest replacement from an older
enabled version. The local profile selects enabled Spotify `0.2.11` and YT
Music `0.2.7`; the user has now confirmed both widgets open in the current fully
packaged Release without weakening immutable package integrity.

The first DLV-052 planner relaunch also exposed a Release-artifact coherence
gap: `OverlayHost.exe` was rebuilt with `-SkipPackaging`, but the changed
WidgetBridge/Runtime outputs beside it were stale, so the old bridge rejected
the native client's new `admitSnapshot` field. A full documented packaging
build from accepted main removes the mismatch and the fresh startup log is
clean. The planner launch contract now forbids `-SkipPackaging` whenever an
accepted milestone changes a managed runtime, bridge, worker, bundled widget,
or packaged metadata output.

DLV-016 is accepted through main `fee1103`. A five-process native semantic
sample now reports bounded snapshot/node/update churn, private resident pages,
input-to-projection latency, repeat range, machine/source/executable provenance,
and broad material-regression gates. A separate current production-host sample
records real Hidden/Visible process-tree CPU, memory, process, timer, and
Direct2D-frame observations. Both explicitly retain unavailable GPU, scheduler,
presentation, controller, private-process-tree, and long-run metrics instead of
converting proxies into claims. This is a credible local foundation for the
DLV-011 pinned-surface comparison, not a universal gaming-performance sign-off.

DLV-006 is accepted through `9c7438f`. Its final existing-host fixture composes
bounded managed-format List/Grid cursor states through bridge parsing,
Direct2D, focus pagination, HWND/UIA projection, refresh churn, error states,
and 2,000/10,000-item logical bounds. Spotify still needs DLV-022 migration
before the reported replacement-page jumps and header oscillation are fixed.

DLV-015 now closes the deterministic real-host accessibility-proof gap. Its
production-host fixture creates one real HWND, publishes the production
`ProviderHost` through `WM_GETOBJECT`, and queries Settings, YT Music, and
Spotify through UI Automation. The retained 183 assertions cover semantic
projection, bounds/order, hidden interactive exclusion, Invoke, RangeValue,
focus, state transitions, and focus restoration. This is credible automated
host evidence, but it does not claim physical Narrator/MSAA or packaged
AppContainer/UIA sign-off.

DLV-010 now closes the immediate external package-journey break: the scaffold
has a matching offline SDK dependency, an executable lifecycle/state/action
snapshot test, and one bounded source build/stage/validate/pack operation. Its
external temporary-directory fixture proves deterministic package bytes and the
two-version install/select/rollback/removal lifecycle without checkout paths.
External publication/API compatibility governance and transactional versioned
template input remain open rather than being implied by this local proof.

DLV-030 now supplies the missing YT Music responsibility proof: the primary
owner drops from 1,365 to 677 physical lines while retaining the only lifecycle,
client, committed-state, invalidation, and disposal authority. Closed action
routing, connection transitions, companion confirmation/rollback and progress,
and snapshot-only presentation are directly tested value seams rather than a
new coordinator graph.

There is substantial good engineering here: the installed-widget runtime uses
an explicit AppContainer and broker boundary; protocol and package inputs are
heavily bounded; managed builds treat warnings as errors; deterministic tests
cover many lifecycle and controller contracts; performance claims are
separated from targets; and the recent SDK work is replacing repeated task,
cancellation, paging, state, navigation, and command plumbing with explicit
public abstractions.

The strongest retained committed evidence remains commit `4450cfa`'s bounded verification gate.
A handshake launcher cannot start the real command until it belongs to a kill-
on-close Windows Job and both capped pumps are active; Job closure reclaims
descendants on success or timeout, and pump completion has its own deadline.
The same commit provides manifest-driven local/Windows lanes, capped streams and
cases, test-project coverage, clean-only release eligibility, exact selected
native compiler/SDK/tool hashes, and SHA-pinned workflow actions. Focused dirty
evidence passes and is correctly rejected for release use. The broadest clean all-lane result
`20260809T141527Z-8946c731` now also passes all 41 steps and 755 JUnit cases in
313.016 seconds for exact clean commit `dc6b092`; the bundle is correctly marked
release-evidence eligible. An immutable hosted artifact remains open. Package-artifact
traversal and hashing now run in a 30-second Job with file, entry, per-file, total-
byte, output, and nested-reparse ceilings. Every provenance command now consumes
the shared remaining `OverallTimeoutSeconds`, and native discovery/hash work runs
through the same bounded launcher. The package helper now requires its root below
the evidence root, rejects a root reparse point, and has per-file, total-byte,
entry, root-junction, reduced-budget, and exhaustion fixtures. Immutable hosted
execution remains to prove the checked-in workflow rather than only the local
runner.

Committed runner repair `7c8a5b8` materially improves that
foundation: it serializes verifier processes with a live handle, captures both
repository endpoints, rehashes final packages, and truthfully rejects two
41-step/784-case dirty `ddb66c2` runs that exercised the then-uncommitted patch.
It fixes the reproduced concurrent-run collision. It does not make the actively
edited checkout immutable: non-verifier
writers ignore the lease and equal endpoints cannot exclude a transient edit.
EQ-027 therefore moves from Open to Partially implemented, with an owned
exact-commit worktree and scope-aware complete-gate result still required.
A separate strong committed improvement is `b2d6f95`'s aggregate installed-
catalog policy. It caps IDs, versions, entries,
accounted bytes, and detected elapsed discovery work and prospectively rejects
installs before publication. The implementation agent reports Catalog 29/29,
Bridge 40/40, Settings 41/41, and the 49-file documentation contract green; the
clean all-lane bundle now retains those passing steps. Follow-up commit
`1c1f8bb` adds cancellation/deadline checkpoints per recursive entry and before
each at-most-64-KiB read. Commit `d2e49a9`'s bridge closures retain only enabled
active
versions, but discovery temporarily allocates full inventories for all accepted
history. Commit `8a46d5f` closes the reproduced control-plane failure with a
separately bounded, manifest-free repair inventory and exact-version retirement
through Settings and CLI. It protects the selected generation while allowing
inactive history to be retired even when Spotify remains enabled. Direct
current-state inspection now finds Spotify enabled and selected at 0.2.10 with
three installed versions, down from the reproduced 19-versus-eight failure.
Maximum-scale full discovery and clean final-HEAD evidence remain incomplete,
so EQ-016 stays partially implemented for performance/provenance rather than
for product recovery.

The earlier verified package-evidence contract remains a strong foundation.
Committed work bounds manifest/metadata consumption, rejects short/extra/changed
tree input, parses the exact manifest bytes included in the digest, and binds
host-compiled GBSS to an exact path/hash inventory. Commit `d2e49a9` extends
that inventory to every package file and introduces a per-start
content lease: it rechecks the exact tree, pins every verified file with
write/delete-denying handles, gives the AppContainer direct non-inheriting grants
only for required directories and files, and retains the lease until process
teardown. This is the first implementation that actually carries verified
content authority across the bridge/runtime launch seam. Clean retained run
`20260809T152831Z-67b77c73` proves the selected Release path at documentation-
only HEAD `6bd60d3` over implementation baseline `6fc9e01`.
Source conformance now routes five real installed
first-party packages through that path and exercises YT Music across suspend,
crash/restart, force reload, update, and removal. It still does not adversarially
prove late/replaced managed/native dependency and asset reads, ACL rollback, or
alternate AppContainer-group authority. Content generations now receive distinct
digest-bound AppContainer identities, and a production-token test proves a new
identity cannot read a root granted to the prior generation. EQ-014 is therefore
materially partially implemented rather than resolved.

Commit `dc30be9` materially repairs the authority-transaction defect found by
this audit. The pending record now lives under the desktop host's Local AppData,
outside the AppContainer-writable profile; the root receives a protected host/
SYSTEM/Administrators DACL, ancestor and entry reparse points fail closed, a
global exclusive file lease serializes transactions, and a flushed pending
record is created before the first DACL mutation. A real child process exits
after a partial apply and the next host restores the recorded descriptors before
clearing the journal. This closes the prior writable-marker and ordinary
process-termination gaps rather than merely renaming quarantine state.

Commit `0ff403a` further binds every snapshot to volume serial plus
128-bit file ID, keeps handle-bound DACL operations alive across capture/apply/
verify/rollback, verifies the post-apply and post-restore descriptors, and
refuses path replacement during recovery. This composes with the production
content lease, which already pins verified directories and files without
delete sharing. Dirty full run `20260810T005210Z-c46e6881` passes 41/41 steps and
793 JUnit cases in 599.821 seconds, including Runtime 58/58, with identical dirty
`dc30be9` endpoints and correct release ineligibility.

EQ-028 remains P1 for recovery isolation and operability. There is one global
pending file for all community packages. The new identity-mismatch case
deliberately retains that record, but every later community start reads and
tries to recover the same record before its own transaction; one moved or
replaced target can therefore quarantine all community widgets indefinitely.
There is no host-owned repair/inspect/clear workflow, and the new alternate-
authority check rejects only the two broad application-package groups rather
than arbitrary other AppContainer SID ACEs. Fail-closed behavior is correct,
but product-wide denial without a verified repair path is not release-ready.

Follow-up `6fc9e01` closes the deterministic package-shape mismatch found by
this audit. Package inspection and installed-tree verification now reject more
than 1,024 package-root/implicit directories required to reach verified files,
the runtime retains the same executable outer limit, and bridge coverage asserts
the two internal constants remain aligned. Clean selected-step run
`20260809T152831Z-67b77c73` retains Catalog 32/32, Bridge 42/42, CLI 49/49,
Runtime 44/44, Worker Host 9/9, First-Party Conformance 5/5, and the
documentation contract green: 182 JUnit cases across seven explicitly selected
steps for clean documentation-only HEAD `6bd60d3`, which contains implementation
baseline `6fc9e01`. It is release-evidence eligible, but its `lane: all` label
must not be read as the complete 41-step manifest because `selectedStepIds`
narrows the run.

The same work exposes a new P1 availability boundary. Its five-second
`ContentLeaseTimeout` covers revalidation only; the subsequent per-directory and
per-file ACL reads/writes, AppContainer setup, and pre-process preparation are
outside that deadline. The new maximum test permits a 512-file startup to take
almost ten seconds. The current clean selected bundle retains one-machine
measurements of 360.426 ms for the 512-file exact AppContainer grant path and
325.283 ms for the 512-file package launch lease; neither is a production
budget or a deep 1,024-directory case. Commit `d4291be` now permits bounded
managed request concurrency, but the only production client still sends one
request and performs synchronous untimed `ReadFile` calls from `OverlayApp`.
A slow or blocked ACL operation can therefore still freeze the overlay before
the worker connect timeout begins; the native host cannot issue the unrelated
request that the new raw pipelining fixture demonstrates. EQ-020 requires one
enforced start-admission budget, cancellable
off-UI-thread bridge I/O, transactional authority cleanup, and responsiveness
evidence.

Commit `4f903b0` materially closes that exact-edge
test gap without pretending it closes the deadline. A 258-file package requiring exactly 1,024
authority directories completes public pack/install/enable and renders a real
first-party widget through the production AppContainer path. A paired
1,025-directory CLI fixture proves pack leaves no output and install publishes
no bytes. Clean retained selected run `20260809T155221Z-ae6e5d8d` passes CLI
50/50, Documentation 1/1, and First-Party Conformance 6/6, recording 376.140 ms
packing and 2,528.883 ms through first validated render. It is release-evidence
eligible for clean documentation commit `b2956ab` over implementation
`4f903b0`, with zero stderr or output truncation. Its three explicitly selected
steps do not update unrelated verification lanes.

Commits `6c5f932` and `7d33ce1` structurally resolve the action-ingress ownership mismatch.
Direct, legacy quick, and controller-resolved actions now enter one bounded
active-lifetime FIFO and acknowledge typed admission instead of provider
completion. Capacity proof waits on a deterministic execution barrier. Public
docs define the legacy catalog QuickAction route as deliberately non-authorizing,
retain protocol-v1 empty acknowledgements and failure names, and reserve exact
capability gesture authority for snapshot-correlated controller input. YT Music
and Spotify removed redundant ordinary-action coordination. Late failures now
enter a bounded native queue with runtime-generation identity; the shell rejects
stale/malformed payloads and never renders or logs exception text. Commit
`7d92dcd` additionally binds generic feedback to the affected widget on both
dashboard and open-widget surfaces and replaces the worker's cross-process
exception text with a fixed diagnostic. Commits `7733a73` and `707f850` replace
the earlier lossy global slot with a catalog-bounded, generation-owned store,
allocation-free lookup, deterministic visibility/deadline transitions, one-shot
expiry scheduling, and a controller-timer fallback.

Commit `ddb66c2` now composes those pieces behind
`WidgetActionFeedbackHost`. Production `OverlayApp` supplies the monotonic
clock, Win32 timer scheduling, and invalidation callbacks; reconciles the
bounded descriptor projection; publishes a complete bridge drain; selects the
dashboard or open-widget identity; and reuses the same Hide/Stop/expiry seam.
Its deterministic 305-check target feeds two widgets through one host adapter
and proves offscreen isolation, one invalidation/schedule per batch, deadline
and controller-timer expiry, generation/catalog retirement, and no resurrection
after Hide or Stop. Dirty schema-v2 full run
`20260809T232451Z-15d2e7b6` retains that target plus 41/41 steps and 784 JUnit
cases. This closes the missing native presentation composition architecture;
EQ-021 remains Verifying only for a real bridge/native controller failure flow,
advanced-widget adoption evidence, and a clean exact-commit bundle.

Commit `689a933` exposes an assurance-process regression. The implementation
stream staged both independent review documents, titled the commit “close
unified action admission review,” and changed EQ-021 to Resolved with focused-
Release claims while the latest retained evidence still predates the action
commits and the native presentation defects above remain. This mixes
implementation, evidence reporting, and independent disposition in one actor
and can also sweep a reviewer's unrelated dirty work into a product commit.
EQ-024 makes the review documents reviewer-owned inputs: implementation may
report evidence and proposed dispositions elsewhere, but must not edit, stage,
or commit these two files.

The bridge-concurrency code also needs one ownership pass before more request
types or an asynchronous native client are added. `WidgetBridgeServer.RunAsync`
now owns capacity, active-ID uniqueness, per-widget receive-order tails, task
tracking, fatal-error propagation, Stop, and drain through six interacting
mutable mechanisms, while `ClientRegistration.OperationGate` separately
serializes widget work. EQ-022 recommends a narrow internal request dispatcher
with deterministic tests; this is not a request for a generic framework or a
line-count refactor.

The accessibility rotation is now a substantial improvement rather than a
provider-absence finding. Commits `b166a01`, `e1a42bd`, and `15e0f77` activate
`WM_GETOBJECT`, publish immutable widget and tray fragment trees, expose Invoke,
RangeValue, SelectionItem, and Selection patterns, convert exact clipped
geometry to screen coordinates, and post bounded generation-checked actions to
the UI thread. A real `IUIAutomation` client test discovers the HWND and reads
both widget and tray elements. Commit `b3acd47` also publishes focus,
structure, name, help, enabled, selection, range, and bounds events. Commits
`f1274a7` and `4c591ed` then close the inspected provider-lifetime, hidden-focus,
root-bounds-event, and optimistic-slider coherence defects with explicit detach,
binding generations, UI-thread-published window state, and one presented-slider
revision shared by pixels, UIA values, and events. Commits `02cc40a`, `943d67b`,
and `0598e5a` separate static help from live status, select tray widgets by
stable ID instead of replaying navigation, and retain real UIA clients through
actual window destruction. Commit `b3558f9` then publishes widget content,
host Back/Close/help/status, and the visible tray in one open-widget tree.
Commit `59aae1a` distinguishes accessibility automation from physical input
through native, bridge, runtime reservation, and base SDK routing. Commit
`6162937` adds collision-proof owner domains and typed nested Back. Commit
`9ec0374` makes worker ambient context physical-only, validates the origin again,
and forwards broker gesture metadata only after exact activation succeeds. Its
adversarial tests target synchronous custom-override use and denied activation
with a pre-existing broker grant. Commit `6a079b6` mirrors the current managed
focused/no-focus, disabled/busy, ancestor, stale-focus, and nested-scope Back
rules. It replaces the reviewed throwing vector path with allocation-free
bounded recursion, and focused native/managed cases cover the same branch set.
EQ-023 remains P1 because the algorithms are still independently owned,
exact-commit clean evidence and real route traversal are absent, and packaged
Narrator/AppContainer proof does not exist.

The live Spotify reverse-pagination failure is now materially addressed by
`5c3ce72` and strengthened by `023ea46`. `MoveWidgetFocus` asks the current Scroll for a page action before
explicit/geometric movement can escape it, and replacement-page
`InitialFocusId` now outranks stale exact/ordinal memory when that value changes.
The new Spotify test drives real compact/expanded 29-item playlist and detail
resources through forward/reverse cache transitions, repeated slow input, IDs,
focus requests, and provider counts; matching native tests use Spotify's exact
scroll/rail IDs and 12/12/5 topology and prove focus consumption on unrelated
refresh. This is valuable cross-layer contract coverage, but it is not composed:
the widget helper manually selects the Scroll action and calls
`OnActionAsync`, while native host tests consume separate synthetic trees.
EQ-026 therefore remains Verifying pending a serialized real-snapshot host/
bridge round trip and the user's live controller retest.

The public authoring entry point now has a coherent local package journey.
Accepted DLV-010 gives `gbar new widget` a matching content-addressed offline SDK
dependency, generated lifecycle/state/action snapshot proof, and source-aware
packaging; its unrelated-directory fixture completes deterministic two-version
installation, selection, rollback, and removal without checkout references.
It remains a local pre-publication workflow rather than a governed externally
published SDK/template release. Accepted DLV-046 now gives the checked-in
template a strict versioned text/binary inventory and an all-or-nothing sibling-
staging transaction. Accepted DLV-047 adds one checked-in bounded public API and
release-unit contract; accepted DLV-050 converts only that new suite to 12 named
`MSTest.Sdk` 4.3.2 cases, moves intentional baseline mutation to a separate
bounded tool, and retains every legacy executable runner. Accepted DLV-048 binds
the exact generated source and eight canonical command phases to the same
external fixture. Generic isolated scenario execution and public release/CI
provenance remain open. The prior generated source-to-package mismatch was
closed by reusing the bounded build/generation owner and validating the staged
entrypoint before deterministic publication.
DLV-046 closes the prior generator defect: `templateVersion` is enforced, every
file is declared with text/binary mode, count/file/aggregate/path/reparse rules
are bounded, and success follows validation plus one atomic rename. Its focused
failure matrix proves malformed inventory, binary preservation, unreadable input,
late validation, cancellation, destination faults, and rollback. The roughly
2,400-symbol generated public surface now has accepted package metadata and an
ordinal 5,000-symbol/1-MiB compatibility baseline. Compatible additions and
removals/signature changes are explicit reviewed diffs; no post-1.0 compatibility
promise or external publication is implied.

Current HEAD closes the remaining CLI author-code bypass: `gbar render`
now accepts only bounded snapshot JSON, and DLL input fails closed before type
resolution or output handling. `gbar dev` remains the executable integration
path through the production AppContainer worker boundary. Current HEAD
also resolves the prior responsive-focus
identity risk by separating focus persistence from action and source-element
routing. It also moves reconciliation out of steady paint and onto relevant
state transitions. The clean all-lane bundle retains the affected managed cases,
41 Focus Navigation checks, 20 Widget Surface Focus checks, the native catalog/
renderer suites, and the OverlayHost Release build for exact commit `dc6b092`.

A separate public-distribution blocker is the incomplete publisher/provenance
model. Current HEAD now makes the immediate Settings decision honest:
it labels Community packages unsigned, identifies the manifest publisher as
unverified, shows the sealed digest, and binds enablement/consent language to
those exact content bytes. It still cannot show a host-owned acquisition
receipt, verified signer, rotation, or revocation because those models do not
exist yet. The deeper launch audit previously found that digest-derived identity
was published before the generic worker reopened mutable assembly and dependency
paths. Commit `d2e49a9`'s launch-lease work directly addresses that defect, including
ordinary assets, without making authors manage hashes. The remaining highest-
risk package-boundary question is whether the Windows ACL implementation remains
exact across all loader and inherited-authority cases. One production-token
runtime case proves a prior broad grant on the current root is replaced and a
late text file is denied. A second proves a new digest-bound content-generation
identity cannot read a stale root granted to the prior identity. Direct grants
remain persistent filesystem metadata, teardown does not revoke them, and no
test yet excludes an alternate inherited AppContainer-group allow ACE.

Current HEAD adds a system-wide application-worker admission
envelope on top of the independent Jobs: eight workers and 512 MiB of declared
Job limits by default, plus one separately accounted trusted Settings control
plane. The follow-up correctly moves reservation lifetime into an exact runtime
process lease instead of sampling pipe-based `IsRunning`, and adds narrow direct
runtime lease tests. EQ-013 remains partial because that evidence is
implementation-reported and does not cover several fault paths, while capacity
refusal is still flattened to a generic bridge error and can leave the native
panel saying `Starting isolated ...` without an in-widget explanation or
controller remediation. No bounded critical-work lease exists, and the
retained baseline still exercises only one selected worker.

The YT Music operation-lane migration removes a substantial amount of manual
lifecycle machinery. Current HEAD also closes its stale-failure gap:
success, retry status, and authorization mutations now share the
`WidgetOperationContext.IsCurrent` contract, including a final check serialized
with state mutation and same-lane admission.

The current repository also carries too much coordination and product policy in
a few very large translation units/classes, and its verification process does
not yet provide a fully proven, reproducible GitHub gate. The extensive
bug ledger has accumulated 55 simultaneously active `Verifying` entries,
including 22 P0s, which makes priority and release status difficult to trust.

The native-host rotation makes that ownership concern concrete. `OverlayApp`
occupies about 3,753 lines and directly composes bridge transport, catalog
generations, snapshot authority, lifecycle, retry, input/focus, presentation,
rendering, and user-visible failure state. The right first extraction is a
tested `WidgetSessionCoordinator` above `WidgetBridgeClient`, not another pure
helper or a broad UI rewrite. Besides reducing change coupling, this is the
natural owner for the typed persistent capacity/startup failures still missing
from the product UX. The current native client intentionally sanitizes an error
message but discards the bridge error code; a failed snapshot refresh leaves any
previous snapshot cached and reports only a four-second action message. The UI
can therefore continue presenting stale controls without a durable distinction
between capacity denial, worker startup failure, protocol failure, and transient
refresh failure.

The test-architecture rotation also made the evidence gap concrete. All 33
managed test projects are custom executable harnesses with a `Program.cs`; none
references `Microsoft.NET.Test.Sdk`. That choice is not itself a quality defect.
Current HEAD now adds the right thin-runner shape: one
41-step managed/native manifest, stable step IDs, per-step and overall deadlines,
capped output and case extraction, process-tree termination, logs, JUnit
conversion, JSON results, test-project inventory checking, and a Windows
workflow with 30-day artifacts. It also labels its dirty digest honestly as a
status digest and makes dirty runs ineligible as release evidence. This is a
reliable local-gate foundation. A supervised handshake closes the command's pre-
assignment escape window, tree and pump teardown are bounded, exact selected
Visual Studio/MSVC/SDK/tool identity is retained, workflow actions are SHA-
pinned, local/hosted retention policy is documented, and package provenance has
process/resource/root-containment caps under the shared deadline. The retained
clean all-lane result closes the local execution portion of EQ-004; immutable
hosted execution proof remains open.

## Changes since the previous audit

Commit `d171dc8` closes the remaining catalog/runtime pathname handoff inspected
in the prior cycle. `InstalledPackageLaunchLease` now opens each directory and
file without following the final reparse point, retains the handle, records its
volume serial and 128-bit file ID, and hands a typed target/identity sequence to
the runtime. `WindowsAppContainer` captures the live ACL targets and compares
every identity with that catalog evidence before it publishes the journal or
changes a DACL. Focused tests cover replacement between the two layers,
malformed/duplicate evidence, handle cleanup, and first-party conformance.
Full run `20260810T014706Z-852d0250` passes 41/41 steps and 796 cases in
367.882 seconds, but its stable endpoints are dirty `0ff403a`; the patch was
committed only afterward as `d171dc8`, so `releaseEvidenceEligible` correctly
remains false.

No corresponding user-facing or author-facing milestone landed. Games & Apps
still reads schema-v1 `SavedIds`, resolves only that allowlist on activation,
and enumerates the catalog only after **Add applications**; removal persists no
exclusion that could distinguish “removed by the user” from “newly discovered
game.” Tray Y still routes immediately to `HostToggleReorder`, while F5 and the
Back+Start recovery chord call a restart routine that accepts only an open
widget. Audio provider commands still cover default input/output volume and
mute, not endpoint selection. The native renderer now has a shared icon-label
placement function and synthetic start/center/end pixel coverage, but no
current packaged visual evidence closes the clipping and optical-alignment
problems visible in the supplied Games & Apps and Spotify captures.

Commit `8a46d5f` closes EQ-016's reproduced recovery-policy defect. A bounded
directory-name/state projection remains available when full catalog discovery
fails a quota, and exact inactive non-selected versions can now be atomically
retired while the selected Spotify generation remains enabled. Catalog,
Settings, and CLI cases use enabled fixtures, protect the selected version,
ignore corrupt candidate manifest bytes, reject cancellation and over-budget
trees before the move, recover normal discovery, and retain the active enabled
generation. Direct inspection of the same local product catalog now finds
Spotify enabled at selected version 0.2.10 with only 0.2.8, 0.2.9, and 0.2.10
installed, rather than the prior 19-version failing tree.

Two subsequent broad Release runs each pass all 41 steps. The newer
`20260809T215159Z-b62d551b` bundle records 783 JUnit cases with zero failures,
errors, or skips, including Catalog 34/34, Settings 42/42, and CLI 51/51. It is
not release evidence: provenance identifies base commit `b15075b`, records a
dirty tree, and sets `releaseEvidenceEligible: false`; final HEAD is `8a46d5f`.
The run's catalog output measures the 512-version repair projection at 278.620
ms and 1,711,440 allocated bytes. `docs/implementation-status.md` instead cites
326.382 ms without a retained result that this review could locate. Treat that
number as an unproven local observation until it is tied to an exact manifest.

Commit `9ec0374` directly addresses EQ-023's remaining inner gesture-origin
mismatch. `WidgetWorkerServer` validates the closed origin and
enters ambient gesture context only for physical dashboard input.
`BrokerWidgetCapabilityClient` attaches sequence/snapshot metadata only after
the exact activation callback returns true and the context remains active. The
new adversarial custom widget synchronously invokes a capability from an
automation-origin override and records no context, activation, provider call,
grant, or revoke. A broker-adapter case starts with a valid pre-granted
authority, denies activation, and expects zero provider calls; that makes the
test sensitive to accidentally forwarding the gesture fields. This is the
right defense-in-depth shape. Dirty broad run
`20260809T220617Z-bc1dd7a3` passes 41/41 steps and 783 cases, including Runtime
49/49, but its manifest is based on dirty `8a46d5f` and is release-ineligible;
it is not exact immutable proof for final commit `9ec0374`.

Commit `6a079b6` substantially improves nested-Back equivalence. It supplies
current focus to publication and invocation
revalidation, rejects focus outside the active scope, lets a focused shortcut
override ancestor behavior, suppresses fallback when that focused owner is
disabled/busy, preserves ancestor fallback when focus has no shortcut, and
handles focusless scope-root disabled/busy state. These cases match the current
managed resolver inspected in `Widget.cs`, and native plus managed tests cover
each branch. The final commit replaces the reviewed `std::vector` path with an
allocation-free `FocusedBackResolution` recursion, preserving the `noexcept`
contract within the protocol's bounded tree depth. A native mirror still needs
a shared conformance corpus or protocol-owned resolution result to prevent
future SDK drift, and no real UIA route traversal exists. Dirty broad run
`20260809T221800Z-2c278bf3` passes 41/41 steps and 783 cases from a dirty
`9ec0374` base containing this work, but is not exact clean evidence for
`6a079b6`.

Commit `023ea46` adds the first Spotify-specific 29-item compact/expanded
12/12/5 regression. It covers playlist and detail resources, absolute IDs,
forward paging, cached reverse paging, joined slow cancellation-ignoring input,
and provider call counts. Native tests add exact Spotify scroll/rail IDs, the
five-row final topology, and one-shot focus consumption across unrelated
refresh. The evidence remains deliberately component-composed rather than
host-composed: `PressPagedDirectionAsync` calculates the adjacent target,
extracts the Scroll action, and directly calls `SpotifyWidget.OnActionAsync`;
the native tests use independently constructed nodes. Detail-track reverse
coverage also stops at final page -> middle page rather than returning to the
first page. Preserve the test, but do not call it the shipped controller route.

The original EQ-027 reproduction remains useful context. Full runs
`20260809T223902Z-17e88fe6` and `20260809T223920Z-37fcdf1a` started 18 seconds
apart in the same checkout. The first failed when CSC could not write
`NetworkControlsWidget.dll` because another process held the shared
`obj/Release` output; the overlapping run passed. Both were dirty and
release-ineligible, but this demonstrated that unique result directories did
not isolate shared build outputs.

Commit `7c8a5b8` materially addresses that defect. A live
file-handle lease is acquired before provenance and held through result
publication; owner metadata is diagnostic only and process death releases
authority. Schema v2 recaptures commit/full status, recomputes package digests,
and requires passing, untruncated, clean, identical endpoints for eligibility.
Its process fixture proves contention before a marker is written, normal
release, and forced-process-death release; pure eligibility cases cover a dirty
finish and changed commit. Full runs `20260809T231747Z-d9e41dcc` and
`20260809T232451Z-15d2e7b6` each pass 41/41 steps and 784 cases without overlap;
the latter takes 326.534 seconds and truthfully reports identical dirty
`ddb66c2` endpoints, `repositoryStateStable: true`, and release ineligibility.
Those runs exercised the then-dirty patch now committed as `7c8a5b8`; no clean
exact-commit bundle exists. More importantly, the lease serializes verifier
processes, not editors, agents, or ordinary build commands; identical endpoints
cannot prove source was never changed transiently during the interval. Treat it
as a shared-output guard, not yet an immutable-source release boundary.

Commits `dc30be9` and `0ff403a` are the strongest improvement in this cycle. The first replaces
`0be052b`'s AppContainer-writable marker with a host-private, reparse-rejecting,
write-ahead journal and cross-process lock, then proves recovery after real host
termination during partial DACL application; the second uses
handle-bound volume/file identity and post-operation verification so a path
replacement cannot redirect apply or recovery. Fresh full run
`20260810T005210Z-c46e6881` passes 41/41 steps and 793 cases, including Runtime
58/58, in 599.821 seconds. It began and ended on the same dirty `dc30be9` status
and is correctly release-ineligible.

The same review exposes the remaining recovery blast radius. The journal uses
one global pending record, and `ReplaceReadAndExecuteGrant` attempts to recover
it before every package's own transaction. The dirty identity-mismatch test
correctly leaves an unrecoverable record in place, but clears it directly in
test cleanup; production has no bounded inspect/repair operation. One moved
target can therefore block every community widget indefinitely. The broad-
authority test also covers only `S-1-15-2-1`/`S-1-15-2-2`, not another specific
AppContainer SID. EQ-028 stays partially implemented rather than resolved.

Commit `7733a73` adds `WidgetActionFeedbackStore`, wires it into `OverlayApp`,
adds a dedicated expiry timer, and registers a pure native test in both CMake
and `build.ps1`. This is a strong response to EQ-021: independent widget
failures no longer share one
slot, exact runtime generation gates presentation, catalog changes forget old
entries, expiry can schedule a repaint with a visible controller-timer fallback,
and transparent string-view lookup keeps the render query allocation-free and
truthfully `noexcept`.

Commit `707f850` adds a pure `WidgetActionFeedbackController`
whose caller supplies monotonic time and receives `shouldInvalidate` plus the
next deadline. `OverlayApp` now uses it for Show/Hide, publish, expiry, catalog
cleanup, and surface lookup; the deterministic test expands to hidden publish,
show without resurrection, deadline transition, and cleanup. This is the narrow
seam the prior review requested. Commit `ddb66c2` adds the production
`WidgetActionFeedbackHost` adapter above it and a 305-check deterministic target
covering the catalog, complete drained batch, dashboard/open selection, timer
callbacks, fallback, Hide, and Stop. The latest dirty full bundles retain that
target and the OverlayHost link. A real bridge-originated advanced-widget
failure still has not traversed the native controller and painted surface.

Commit `5804eaa` adds Spotify to the ordinary bundled and installed first-party
conformance set. A simulated connected Spotify backend now renders a real
playback snapshot inside the generic AppContainer worker, and the fixture sends
`spotify.next` through `WidgetProcessClient.SendActionAsync`, waits for the
broker command, and verifies `SpotifyPlaybackOperation.Next`. This closes the
previous absence of any packaged Spotify provider-command path. It proves the
generic worker/runtime admission and broker authority path, not bridge/native
controller ingress or late native failure presentation. The implementation
agent reports First-Party Conformance 6/6 green; current retained evidence still
predates the commit.

Commits `7733a73`, `5804eaa`, `707f850`, `57bc4c9`, `b3acd47`, `f1274a7`,
`4c591ed`, `79308bf`, `02cc40a`, `943d67b`, `5c3ce72`, `0598e5a`,
`b3558f9`, `59aae1a`, `6162937`, `8a46d5f`, `9ec0374`, `6a079b6`,
`023ea46`, `ddb66c2`, `7c8a5b8`, and `0be052b` update
`docs/implementation-status.md` while leaving both reviewer-owned documents
untouched, so EQ-024 did not regress across these milestones.

Clean release-eligible bundle `20260809T201448Z-0249ae81` is a complete
41-step run for exact clean commit `0598e5a`: 41/41 steps and 778 JUnit cases
passed with zero failures, errors, or skips. It covers the action queue and
feedback controller, Spotify packaged action, paging repair, quiet dashboard,
direct tray selection, actual-destroy UIA client case, native build, and broad
repository baseline. It predates composite-shell commit `b3558f9`, explicit-
origin commit `59aae1a`, identity/Back commit `6162937`, and catalog-recovery
commit `8a46d5f`, gesture-origin commit `9ec0374`, Back-equivalence commit
`6a079b6`, Spotify-proof commit `023ea46`, and feedback-host commit `ddb66c2`.
It also predates runner commit `7c8a5b8` and authority-transaction commit
`0be052b`.
The newest schema-v2 broad runs are non-overlapping but dirty/ineligible; the
last clean full-gate evidence remains `0598e5a`.

The rotated advanced-widget audit adds EQ-025. Spotify's paged-resource
migration rejects stale completions within each resource, but `Render` combines
manual state and two resource snapshots from unrelated revisions. The selected
playlist is also the mutable, implicit input to an unkeyed detail resource.
Existing tests cover slow completion after Back but not the exact
selection-commit/resource-reset interleaving or a source-key invariant.

A user-observed Spotify run added EQ-026 after the final partial playlist page
left only five rows and Up did not restore the preceding page. Commit `5c3ce72`
now checks the original focused Scroll before ordinary movement and prioritizes
a changed replacement-page focus request over stale memory. This directly
addresses both inspected seams. Commit `023ea46` now exercises the real widget's
29-item resources and matching native topology, but manually bridges those
pieces rather than driving the shipped controller/bridge route, so the finding
is still Verifying rather than resolved.

Commit `57bc4c9` begins EQ-023's projection layer. Subsequent commits through
`0598e5a` activate widget/tray/dashboard providers and events, share tray
geometry, detach retained providers with binding generations, align optimistic
slider pixels with UIA, make ordinary help non-live, select tray items directly,
and prove retained real-client roots become unavailable after `DestroyWindow`.
Commit `b3558f9` adds one composite open-widget shell. Commit `59aae1a` makes
automation origin explicit and physically sourced dashboard authority narrower.
Commit `6162937` adds owner-domain identity and nested Back for the two prior
composition defects. Source review now finds the remaining gap one layer deeper:
the worker enters ambient gesture context without checking origin, and nested
Back publication does not yet share or fully mirror managed action resolution.

Commit `d4291be` moves ordinary request execution off the sole pipe-read loop,
admits at most 16 requests, preserves framed writes, explicitly chains same-
widget requests in receive order, rejects excess work with `bridge_busy`, fails
duplicate active request IDs closed, and tracks/drains requests on session exit.
Clean retained selected run `20260809T161934Z-5d0bee6a` executes the Bridge
harness at 45/45, Documentation at 1/1, and First-Party Conformance at 6/6 in
75.643 seconds for exact commit `fdcf253`. Its manifest records a clean tree,
`releaseEvidenceEligible: true`, and zero stderr/truncation. The three new cases
block a cancellation-aware admission and prove
an unrelated catalog response, the seventeenth-request saturation error, Stop
acknowledgement, cancellation/resource release, pipelined same-widget ordering,
response correlation, and duplicate-ID refusal. They close only the cooperative
managed head-of-line subproblem: the real ACL path is
synchronous, up to 16 cancellation-ignoring operations can still be stranded,
and shutdown drain has no separate deadline.

The production native client is also not the pipelined client exercised by the
new fixture. Every `WidgetBridgeClient` method writes one request, synchronously
reads frames until its response arrives, and rejects a different non-event
request ID; `OverlayApp` calls those methods from window-message and presentation
paths. The managed scheduler therefore creates protocol capacity that the
shipping caller cannot use to keep B/Guide/Close, catalog recovery, or another
widget responsive during a blocked request. A future asynchronous client will
need one read owner plus a correlation table; concurrent calls to the current
methods would race pipe reads and treat valid out-of-order responses as protocol
failures.

The same cycle rotates into advanced-widget action dispatch. Static tracing
from native/bridge ingress through worker acknowledgement and the public
loopback timeout establishes the new EQ-021 finding; `d4291be` does not change
that synchronous direct-action acknowledgement contract.

The code-quality rotation adds EQ-022. `RunAsync` now combines transport/session
ownership with admission capacity, duplicate-ID state, per-widget task tails,
completion continuations, fatal-error arbitration, and drain. The three
integration-style cases are valuable, but they do not give the scheduler a
deterministic, cross-platform test surface for every completion and cleanup
path.

The accessibility rotation originally added EQ-023 after finding no operating-
system provider. Commits through `0598e5a` provide widget/tray/dashboard UIA,
shared geometry, closed patterns, generation-checked dispatch, events, explicit
provider lifetime, coherent slider values, quiet help/status, direct tray
selection, and actual-destroy real-client coverage. Commit `b3558f9` adds the
composite shell, `59aae1a` adds explicit origin, and `6162937` commits collision-
proof identity plus nested-scope Back. The finding has moved to inner-layer
origin enforcement, route equivalence, typed choices, and packaged proof; the
latest retained clean aggregate stops at `0598e5a`.

Commit `e7b4e6b` is documentation-only. It correctly carries retained run
`20260809T152831Z-67b77c73`'s 325.283 ms launch-lease and 360.426 ms exact-grant
measurements into implementation status, roadmap, and packaging guidance. The
run remains seven explicitly selected managed steps for clean docs commit
`6bd60d3`, not a current complete 41-step managed/native gate. That
documentation-only commit did not change implementation finding status.

Commit `4f903b0` lands those two implementation test files. Code inspection shows
an exact 1,024-directory/258-file package carried through public pack, install,
enable, lease-count validation, and a real AppContainer first render, plus an
exact 1,025-directory CLI refusal that asserts no archive or installed package
publication. Clean retained selected result `20260809T155221Z-ae6e5d8d` passes
57/57 cases across CLI, Documentation, and First-Party Conformance in 95.533
seconds for clean documentation commit `b2956ab` over implementation `4f903b0`.
It records 376.140 ms packing and 2,528.883 ms through first validated render,
with zero stderr or output truncation, and is release-evidence eligible. Its
three explicitly selected steps are not a complete all-manifest run.

The verification follow-up is committed as `4450cfa`. The runner starts a small
handshake launcher, assigns it to a kill-on-close Windows Job, starts both capped
pumps, and only then authorizes the real command. Children inherit the Job, so
user code has no pre-assignment escape window. Job closure occurs on success or
timeout, and combined pump completion has an independent five-second deadline.
The self-test starts a child without waiting, expects the parent step to pass
within ten seconds, and confirms the child PID is gone; the ordinary timed parent/
child case also remains green. Focused run `20260809T141114Z-628dd67c` passed
runner self-test, WidgetTicker, and Documentation in 15.396 seconds. It exercises
the bounded package/helper quota and root-junction cases plus shared-deadline/
native-provenance work and is intentionally not release evidence because its
source tree was dirty; it names pre-amend `442250f` rather than current HEAD.

The later clean all-lane run `20260809T141527Z-8946c731` is the first retained
release-eligible result for the new runner. It passed all 41 manifest steps and
755 JUnit cases with zero failures, errors, or skips in 313.016 seconds against
exact commit `dc6b092`. The bundle contains 41 JUnit files, per-step command and
stdout/stderr records, 29 Community package digests, MSVC 14.51.36231 and
compiler 19.51.36248.0 plus its hash, Windows SDK 10.0.26100.0 for both native
paths, and the manifest-tool version/hash. This review did not launch the run;
it inspected the retained aggregate, per-step statuses, JUnit totals, native
build/smoke artifacts, and provenance. No referenced hosted workflow execution
was found.

Commit `d2e49a9` landed after the prior audit and materially changes the
installed-package launch chain. Catalog verification
captures a SHA-256 for every exact relative path from the same bounded reads as
the tree digest. `InstalledPackageLaunchLease` re-enumerates that inventory,
rejects replacement or insertion, rehashes through restrictively shared handles,
pins verified files/directories, and returns exact ACL inputs. `BridgeCatalog`
supplies the factory to `WidgetBridgeServer`; `WidgetProcessClient` reacquires it
for every start/restart, replaces the broad package-root AppContainer grant with
direct non-inheriting directory/file grants, and disposes the lease after process
teardown. Catalog and bridge tests were added for inventory hashing, handle
pinning, insertion/mutation refusal, exact factory wiring, and sanitized
admission failure. Runtime additions also prove content admission releases
residency before launch, content-lease reacquisition/release across crash,
restart, and stop, and a real AppContainer worker cannot read a late text file
after the same SID's prior broad grant on the current root is replaced. The
existing `InstalledWidgetAuthority` already binds AppContainer identity to the
verified content digest; new focused bridge and production-token cases prove the next generation
uses a distinct identity and cannot read the prior granted root. This review
also found the first-party conformance harness now runs five real
installed packages through the generic content-lease path and carries YT Music
through suspend, restart, force reload, update, and removal. This review
inspected the committed source but did not execute the focused suites.

Follow-up `6fc9e01` rejects archive and installed-tree shapes that would require
more than 1,024 exact authority directories, before extraction/publication or
worker start. Catalog/runtime constants are checked together by bridge coverage;
retained Release verification records Catalog 32/32, Bridge 42/42, and CLI 49/49
in `20260809T152831Z-67b77c73`. That clean release-eligible bundle contains 182
passing JUnit cases across seven explicitly selected managed steps for docs-only
commit `6bd60d3`; it validates the `6fc9e01` implementation baseline but is not
a replacement for a current complete 41-step managed/native run.

EQ-014 remains the highest overall risk, but its status advances to materially
partially implemented. The installed-package conformance path proves ordinary
positive startup/lifecycle behavior, while the production-token negative case
proves one late text file is denied. Neither adversarially proves late or
replaced managed/native dependency and ordinary-asset reads. AppContainer ACLs
persist after the content lease is disposed, but distinct digest-bound
generation identities prevent a new version from inheriting the prior SID's
root grants. The implementation still purges only that SID's ACEs, not alternate
inherited AppContainer-group allow rules. Exact authority therefore still needs
end-to-end loader abuse cases, directory rename/reparse races across each
path/open/ACL phase, and an explicit protected catalog/alternate-ACE contract.

The launch-performance audit found that the advertised five-second admission
deadline ends when the content factory returns. Exact ACL application then runs
synchronously for every verified directory/file with no token or remaining
deadline, before the three-second worker connect timer exists. The current clean
selected run retains 360.426 ms for the 512-file exact-grant path and 325.283 ms
for the separate launch-lease path, while the test asserts only that startup
stays below ten seconds; that threshold is not one production start budget.
The managed bridge can now dispatch another pipelined request while this work
runs, but the native client waits with synchronous untimed `ReadFile` calls from
the UI path and does not pipeline. EQ-020 therefore still records the unenforced
latency claim, native overlay freeze, production control-plane availability, and
partial-ACL rollback requirements.

Follow-up `6fc9e01` closes the deterministic acceptance mismatch. The installer
counts canonical implicit ancestor directories before extraction, installed-tree
verification rechecks the same 1,024 ceiling, the launch lease defends it again,
and bridge coverage asserts the catalog/runtime constants match. Because `gbar
pack` validates its temporary archive through this installer before publishing
the output, pack and install now share the refusal. The focused negative case
constructs 1,025 required directories while staying below the file-count limit.
Exact-boundary acceptance and a deep package's complete pack/install/launch path
remain evidence gaps, not an open format-design mismatch.

At the pre-DLV-010 baseline, the external-author workflow rotation made EQ-015
more concrete. The generated README's first two commands could succeed
while leaving the manifest-declared `payload/<WidgetName>.dll` absent. `gbar
dev` hides that mismatch by building into its own temporary package generation;
`gbar pack .` correctly rejects the same source tree as `missing_entrypoint`.
The focused scaffold test executes only `dotnet build`, while the CLI README
advertises validate, dev, replay, pack, and install as one sequence. The starter
therefore has no tested source-to-release path even for a contributor who
supplies the checkout SDK.

The performance rotation also examined an existing compatibility path. When GameInput legacy-device
tracking is unavailable, startup arms `kGuideCompatibilityTimer` at 25 ms even
while the overlay is hidden; each tick calls the dynamically resolved XInput
Guide-state function for all four slots. The retained baseline observed 31.65
such timer messages per second, implying about 126.6 state probes per second on
that machine, but did not measure scheduler wakeups or Guide latency. EQ-019
records the required adaptive policy and ETW/hardware evidence.

The same commit implements EQ-004's recommended runner shape.
`verification-steps.json` defines 41 stable managed/
native steps, including WidgetTicker; `Verify.ps1` adds lane and step selection,
per-step/overall deadlines, JSON provenance/results, package digests, and per-
step JUnit/log artifacts. The runner now drains through a capped stream pump,
defaults each stream to 4 MiB, limits parsed cases to 10,000 and individual case
lines to 8,192 characters, records truncation, and prevents a nonzero process
from producing green JUnit merely by printing `PASS`. Failed post-timeout
termination is capped at five seconds, test-project coverage is checked, dirty
runs are explicitly ineligible as release evidence, and a Windows workflow runs
both lanes and retains artifacts for 30 days. Actions are pinned by immutable
commit SHA. Provenance now records the exact selected Visual Studio installation,
MSVC directory, compiler version/hash, overlay/InputProbe Windows SDK versions,
manifest-tool version/hash, and GitHub runner-image identifiers. Self-tests cover
success, nonzero exit, high output, ordinary and detached descendant trees,
case limits, JUnit conversion, reduced shared budgets, and typed fail-before-
launch exhaustion. The focused result above records 29 package digests, MSVC 14.51.36231, compiler
19.51.36248.0 plus its SHA-256, Windows SDK 10.0.26100.0 for both native paths,
and the manifest-tool version/hash; the later clean result retains the same
fields while covering every manifest step.

The package-provenance follow-up moves recursive discovery and hashing into the
same Job-backed runner with a 30-second timeout. `Get-PackageProvenance.ps1`
defaults to 4,096 traversed entries, 256 archives, 72 MiB per archive, 2 GiB
aggregate bytes, a 4 MiB output ceiling, and skips nested reparse points. The
self-test proves exact bytes/hash for one fixture and rejects one per-file
overflow. Focused dirty run `20260809T135956Z-1d3f0e61` passed that self-test,
WidgetTicker, and Documentation in 13.121 seconds; it names pre-amend `ac82f4e`
and is correctly clean-ineligible.

The final follow-up now computes remaining aggregate time before Git, .NET,
package, native-toolchain, and manifest-step subprocesses and uses the smaller
of that remainder and each local ceiling. Native discovery and its at-most-128-
MiB compiler/tool hashes also moved into a ten-second Job-backed helper. This
closes the shared-deadline defect in code. The package helper now also requires
its configured root to remain below the evidence root and rejects a root reparse
point. Focused self-tests cover per-file, aggregate-byte, traversal-entry, and
root-junction refusal. The same runner-owned timeout helper has direct tests for
reduced remaining time and typed fail-before-launch exhaustion.

The implementation agent removed reflection, provider loading, timeout tasks,
snapshot output, and the scenario fixture assembly from `gbar preview`.
Manifest listing remains assembly-free; `--scenario` returns a fixed failure
before resolving the declared DLL; and the CLI help, authoring guide,
quickstart, documentation index, and focused tests now describe this fail-
closed boundary. `gbar render` also removed its reflection/load-context path,
making every CLI semantic-inspection path data-only. This resolves EQ-001.
The first snapshot-ceiling revision checked `FileInfo.Length` before reopening
the path for `ReadAllBytesAsync`. The current source corrected that review
finding with one restrictively shared stream and a limit-plus-one read over the
bytes actually consumed; EQ-012 records the verified closure.

The responsive implementation now gives each shell destination a bounded
`focusPersistenceId` shared only by its compact and rail presentations.
Distinct destinations may continue sharing an action ID and use source element
identity for routing. EQ-003 through EQ-006 remain open.

The documentation fixes now use a valid quickstart glyph, explain explicit
focus persistence correctly in SDK Gallery, and accurately enumerate additive
snapshot protocol versions 1 through 13. The remaining proof gap is systemic:
copyable C# examples are not compiled as external SDK consumers.

A deeper controller audit initially treated disabled destinations as
non-focusable. Source and focused tests show the opposite is the deliberate
platform contract: disabled and busy controls remain navigable so users can
inspect their label and unavailable state, while activation is suppressed.
EQ-009 records that correction and closes without a code change.

The YT Music migration now schedules transport reconciliation through one
runtime-owned Active Latest lane and removes its task set, cancellation source,
generation, lock, and continuation cleanup. Attempt-local success and failure
commits now check current operation identity under the same state lock used for
replacement admission. Cancellation-ignoring fake requests cover superseded
ordinary and authorization failures plus lifecycle-exit authorization failure.

The package/distribution audit found strong byte-integrity mechanics but an
incomplete publisher/provenance model. Settings now makes the present unsigned
decision honest: it labels the publisher unverified, shows the full sealed
content digest, binds consent copy to that digest, and says **Enable unsigned
widget**. Host-owned acquisition receipts, signatures, signer rotation, and
revocation remain open under EQ-011.

The implementation agent has advanced EQ-013. `WidgetProcessClient` now acquires
a host-owned residency lease immediately before process creation and disposes
it when that exact process exits or its pipe/process/Job session is detached.
`WorkerResidencyBudget` defaults to 8/512 MiB, refuses application overcommit,
and keeps one exact trusted Settings worker separately accounted so capacity
pressure cannot remove the control plane. Focused tests cover concurrent N+1
admission, count and memory refusal, one natural crash, and Settings admission.
Direct runtime tests now prove pre-launch admission denial and exact lease
release across a natural crash/relaunch and cooperative stop. Bridge tests cover
timeout and idle-unload release/reacquisition. Failed connection,
invalid-protocol/snapshot, companion failure, a disconnected pipe while its
process remains live, disable/removal, catalog replacement, and shutdown still
lack direct lease accounting proof. Per-widget UI/remediation,
eligible-worker reclaim/override, critical-work leases, and multi-widget
measurements remain open.

The follow-up exact-byte audit confirmed that enablement and bridge publication
both recompute the sealed tree digest. Current HEAD replaces both
stat-then-reopen metadata paths with bounded single-handle consumption and adds
exact-length tree hashing. Three focused cases cover exact/misreported helper
streams, the `int.MaxValue` arithmetic path, and static oversized manifest/
metadata error mapping. The implementation agent reports focused Release
catalog coverage at 28/28, now including changing seekable length, non-
seekable limit-plus-one input, and an owned verified-manifest copy; this review
did not execute it. Discovery now parses that hash-captured copy rather than
reopening the manifest. More importantly, verification still returns a mutable
package path rather than a content lease. Current HEAD removes the
bridge's `FileInfo.Length`/`File.ReadAllText` split: catalog verification emits
per-source SHA-256 values for the exact GBSS inventory, and installed entries
and imports must match that inventory after one bounded strict-UTF-8 read.
Styling Release coverage is implementation-reported at 23/23. The parser's
character ceiling is now applied only after the consumed-byte ceiling.
The worker later loads its entry assembly and
dependencies by path under the previous digest-derived authority. The 175 ms
watcher and stable-tamper retirement test detect change after the fact; they do
not atomically bind the policy or executed bytes to the verified authority.
EQ-014 tracks both boundaries.

The authoring rotation found that the generator is not yet a shipped external
contract, but current HEAD implements the recommended honest interim
boundary. It resolves the SDK before writing, adds `--sdk-project`, removes the
unavailable preview `PackageReference`, and adds an external-directory build
test. It still does not publish a cloneable SDK dependency, exercise a packaged
CLI/template without `GBAR_TEMPLATE_ROOT`, or provide the typed-fake snapshot
exporter the README references. EQ-015 tracks the remaining versioned external-
developer contract.

Commit `5e3dbd1` materially advances EQ-014 with a verified GBSS inventory and
digest-bound reads. The
same inspection identified the unbounded aggregate catalog cost tracked by
EQ-016. The rotating native audit also deepened EQ-003 by identifying the
missing host-side widget-session owner above the already cohesive bridge
transport. No native code change is claimed.

Commit `0d4eb80` resolves EQ-017's false-missing contract by replacing boolean
GBSS source reads with typed statuses, closing result construction behind
validated factories, containing non-fatal custom-provider exceptions, and
routing CLI file validation through the bounded reader. Installed digest/change
failures still collapse to a generic bridge style warning; that cross-layer
integrity-state gap remains tracked by EQ-014.

Commit `b2d6f95` materially advances EQ-016. It adds explicit
ID/version/entry/byte/time options, bounded N+1 directory enumeration, checked
verified totals, and prospective install refusal. The implementation agent
reports Catalog 29/29, Bridge 40/40, Settings 41/41, and the 49-file
documentation contract green; the clean all-lane bundle now retains each of
those passing steps. Commit `1c1f8bb` subsequently threads the discovery
checkpoint
through recursive tree inspection, manifest/metadata reads, filename
enumeration, and every bounded hash read. Full closure still needs a Settings/
CLI repair route that works when full discovery is over limit,
active-only publication inventory ownership, and maximum-scale measurements.

## Verification snapshot

Implementation-reported bounded local Release results on current HEAD
are:

| Scope | Result |
| --- | ---: |
| Widget SDK | 84/84 passed |
| Gbar CLI | 49/49 passed on the typed-diagnostic milestone |
| Settings Widget | 41/41 passed |
| SDK Gallery | 6/6 passed |
| YT Music | 48/48 passed |
| Widget Catalog | 29/29 passed on commit `b2d6f95` |
| Widget Styling | 23/23 passed on current HEAD |
| Platform Settings | 15/15 implementation-reported on the typed-diagnostic milestone |
| Focus Navigation | 41 checks passed |
| Declarative Renderer | 4,632 checks passed |
| Widget bridge catalog | 40/40 passed on commit `b2d6f95` |
| OverlayHost Release target | built successfully |
| Documentation contract | 49 Markdown files passed on commit `b2d6f95` |
| Full bounded all-lane gate | clean release-eligible run `20260809T141527Z-8946c731` passed 41/41 steps and 755 JUnit cases in 313.016 seconds for commit `dc6b092` |
| Current launch/package focused gate | clean release-eligible run `20260809T152831Z-67b77c73` passed 182 cases across seven explicitly selected steps for docs-only commit `6bd60d3` over implementation baseline `6fc9e01`; it is not a complete all-manifest run |
| Exact directory-edge focused gate | clean release-eligible selected run `20260809T155221Z-ae6e5d8d` passed CLI 50/50, Documentation 1/1, and First-Party Conformance 6/6 in 95.533 seconds for documentation commit `b2956ab` over implementation `4f903b0`; zero stderr/truncation, but not a complete all-manifest run |

The typed-diagnostic milestone is committed. Styling 23/23, CLI 49/49, and the
49-file documentation contract were run and their output inspected around the
final API-shape change; Platform Settings 15/15 and Bridge 40/40 were reported
green earlier in the same milestone. The complete 41-step verifier run predates
the current implementation. The new seven-step retained run provides current
evidence for the launch/package path, but not current native-overlay, SDK,
advanced-widget, provider, Settings, or renderer evidence.
The later clean three-step retained run covers the new exact accepted/refused
edge and is release-evidence eligible, but its narrow selection does not update
any unrelated lane.

The retained clean all-lane result exercises the managed, protocol, capability,
documentation, packaging-conformance, native input/layout/rendering, hidden
OverlayHost smoke, and InputProbe paths. It binds those results to exact clean
commit `dc6b092`, the manifest digest, package digests, and selected native
toolchain, and is explicitly release-evidence eligible. This review inspected
the aggregate result, all per-step statuses, and the 41 JUnit files rather than
launching the commands. EQ-004's local runner/evidence portion is implemented;
only referenced immutable hosted execution remains open.
This milestone did not verify a real controller, real YTMDesktop2 companion,
Spotify authentication, physical mixed-DPI display, or PresentMon/ETW trace.
This review visually inspected the retained Spotify setup, accessible setup,
Games & Apps wide, and ad hoc overlay-window captures. The two widget-body
setup views are legible and internally consistent, but the retained bundle is
from a 44-entry dirty tree at old revision `f3ac48c`, packages Spotify 0.1.6
instead of current 0.2.10, and explicitly excludes shell/window, focus/input,
transitions, compositor, and physical-display fidelity. Available default YT
Music and SDK Gallery packages likewise predate their current manifests.
The retained performance baseline is one dirty-worktree run with one selected
Settings worker and only 31 observations per state; it does not measure
multi-widget accumulation, scheduler wakeups, GPU/game-frame impact, controller
latency, or long-run churn.
The unsigned-review Settings changes are committed, and the clean all-lane
bundle retains their 41/41 result.
The residency budget, runtime lease, scaffold residency default, and focused
tests are committed in `7722763`. The clean bundle retains 36/36 runtime and
40/40 bridge cases, including the named direct lease/admission cases.
The committed scaffold milestone adds an outside-repository negative/override
generation case and invokes `dotnet build` on the generated project. This
review inspected the source and retained passing CLI JUnit case rather than
launching it. The case still supplies the template through
`GBAR_TEMPLATE_ROOT`, references the repository SDK project, and does not run
the generated README's render/replay, validate, test, or package commands.
The bounded installed-file reader, its two adoptions, and 27 focused catalog
cases are committed in `7d60acc`. Manifest pairing and the 28th case are
committed in `dadd40d`, which the implementation agent reports passing; this review
code-inspected but did not execute the suites or inspect retained output. The tests
cover exact/extra/truncated/changing-length seekable streams, non-seekable
limit-plus-one input, safe `int.MaxValue` sentinel arithmetic, exact-length
hashing, and static oversized manifest/metadata error codes. They do not
exercise coordinated path replacement or the digest-to-launch boundary.
The GBSS-only inventory, bridge adoption, bounded strict-UTF-8 source reader,
and Styling cases are committed in `5e3dbd1`. The implementation agent reports
`WidgetStyling.Tests` at 23/23; this review inspected but did not execute them.
The cases prove static/misreported/changing-length consumed-byte bounds, digest
mismatch, invalid UTF-8, a positive digest-bound multi-import package, and
rejection of one late-added import. The implementation agent
also inspected a green 283.5-second full Release gate, then reran Catalog 28/28,
Styling 23/23, and Bridge 40/40 after narrowing retained hashes to GBSS only.
The cases do not include a catalog-to-bridge race seam, aggregate installed-
version limits, or a verified package launch lease. The newer clean all-lane
bundle now retains current-commit full-verifier output but does not add those
missing package-boundary tests.

## Prioritized findings

### EQ-001 — P0 — CLI inspection executed author code outside the production sandbox

**Status: Implemented in current HEAD; the clean all-lane bundle retains the
relevant 49/49 CLI cases.**

**Implementation.** `gbar render` now accepts only `.json` snapshots. It checks
the file length against the 4 MiB absolute transport ceiling before allocation,
validates the protocol tree, and prints or canonicalizes only that data. A DLL
input returns a fixed operation error before inspecting `--type`, resolving an
assembly, or touching `--output`. Reflection, `AssemblyLoadContext`, widget
construction, and direct `Render()` invocation were removed from the CLI.

`gbar preview` likewise validates and lists bounded scenario declarations
without resolving the declared assembly. Selected execution fails closed until
a forcibly terminable isolated worker exists. `gbar dev` is now the only CLI
path that executes widget code, and it routes the package through the generic
AppContainer worker, Job, bounded IPC, lifecycle, broker, and renderer boundary.

**Why it matters.** A data-inspection command should not silently grant a
downloaded assembly the user's ambient filesystem, network, process, and token
authority. The previous collectible load context organized dependencies but
provided neither containment nor forcible termination and could also hide
production-only AppContainer failures.

**Tradeoff.** Authors cannot generate a snapshot directly from a DLL until an
isolated scenario/preview worker exists. Typed-fake tests can persist data-only
fixtures for deterministic render/replay, while `gbar dev` covers executable
integration today. This is an intentional workflow gap rather than a full-trust
escape hatch.

**Resolution evidence.** Clean result `20260809T141527Z-8946c731` retains a
49/49 `GbarCli.Tests` JUnit result, including named cases for rejecting assembly
rendering/unbounded snapshots and fail-closed scenario execution. Source
inspection confirms that the render
regression supplies an existing invalid DLL, a type name, instance, and
pre-existing output sentinel; it receives only the fixed isolation diagnostic,
does not leak assembly/type details, and leaves output unchanged. A second case
rejects an oversized snapshot before deserialization. Scenario tests prove a
missing provider assembly can be listed, selected execution remains fail-
closed, and no snapshot output is written. CLI help, quickstart, authoring,
publishing, security, troubleshooting, and sample docs now direct executable
work to `gbar dev` and reserve `gbar render` for bounded data.

### EQ-011 — P1 — Unsigned packages lack acquisition provenance and signed publisher identity

**Status: Partially resolved in current HEAD. Honest digest-bound unsigned
review is implemented; exact verified-byte launch binding is tracked by
EQ-014, while provenance and signed publisher trust remain open.**

**Evidence.** The manual unsigned lifecycle is real rather than aspirational.
`InstallCommand` accepts a local package, exact HTTPS URL, or deterministic
`github:owner/repository@tag/asset` shorthand and requires `--sha256` for remote
bytes. The downloader applies bounded HTTPS/redirect/size/time rules and keeps
the temporary file locked through validation. Installation leaves remote
packages disabled. `WidgetCatalogService` rejects updates while an ID is
enabled, keeps versions immutable, requires explicit version selection, and
supports rollback and disabled-only uninstall. The CLI suite registers exact
GitHub resolution, update/select/rollback/uninstall, unsafe-source, redirect,
size, encoding, timeout, hash-mismatch cleanup, and integrity cases. This review
inspected those tests but did not execute them. `InstalledPackageIntegrity`
seals the extracted content tree, while
`InstalledWidgetAuthority.PublisherId` derives an `unsigned.<digest>` runtime
authority so a replacement observed during catalog validation receives a new
identity instead of inheriting consent or secrets. These are strong integrity
and containment foundations. EQ-014 now records a complete file inventory,
per-start revalidation, pinned handles, and exact non-inheriting grants across
the launch seam; its remaining adversarial loader, object-binding, alternate-ACE,
and partial-grant cases stay owned there rather than being restated as a simple
mutable-path reopen.

They do not establish author identity. `WidgetPackageInstaller` only verifies
that a manifest ID falls within its manifest-declared publisher namespace. The
current HEAD corrects the immediate UX: `InstalledWidgetsSection` renders
**Trust: Unsigned · publisher unverified**, labels the publisher as a manifest
claim, shows the full `InstalledWidgetVersion.ContentDigest`, and uses **Enable
unsigned widget** with exact-byte/capability review copy. Version rows add a
digest prefix, require selection while disabled, and return to full details
before enablement. Permission and confirmation pages repeat the trust state and
full digest; consent remains bound to the digest-derived authority. The catalog
still retains no host-owned acquisition source or signed provenance receipt,
and there is no signature or signer state.

**Why it matters.** AppContainer and broker isolation reduce the consequences
of malicious code but do not make an arbitrary publisher safe. A user cannot
meaningfully distinguish an impostor, a compromised release, or two different
unsigned builds from the controller review surface, even though the UI frames
enablement as an identity review. This blocks a credible public GitHub widget
ecosystem and makes later incident response or revocation impossible to explain
from installed state.

**Underlying problem.** Byte integrity, runtime authority, acquisition
provenance, publisher identity, and user trust are separate concepts. The UI now
exposes the honest unsigned byte-identity contract, but the catalog still lacks
a host-owned acquisition receipt and any signed publisher/update model.

**Recommended direction.** Persist a bounded host-owned acquisition receipt
outside package-controlled content
(local vs canonical GitHub coordinates or a sanitized HTTPS origin/path,
never credentials, query data, or a full local path; plus transferred SHA-256,
install time, and sealed content-tree digest). Add an explicit capability delta
for version changes. Do not imply that a manifest namespace or GitHub owner is
a verified identity.

Before public distribution, define a signed package/update envelope over the
canonical package digest, publisher key identity, package namespace, version,
and expiry/rollback policy. Add key enrollment/rotation, revocation and
compromise handling, signed update metadata, and a Settings distinction between
verified, unsigned-development, unknown-key, invalid, and revoked packages.
Consent inheritance must remain bound to a verified signer plus exact declared
authority policy; unsigned content must retain the current digest-specific
behavior. Keep developer-mode unsigned installation available behind explicit
copy rather than weakening the production trust state.

**Tradeoff.** Persisting source metadata cannot prove that the source is
trustworthy, and a digest shown on the same screen is not an independent trust
channel. It still provides auditability and honest terminology. A centralized
PKI/marketplace simplifies discovery and revocation but creates operational and
moderation ownership; self-managed signing is easier to bootstrap but needs a
clear key-verification and rotation experience. Neither should delay fixing the
misleading unsigned enablement copy.

**Resolution evidence.** The implementation agent reports that Release
`SettingsWidget.Tests` passes 41/41; this review did not rerun it or inspect a
retained result. Source inspection shows focused
cases prove Community packages are never rendered as verified, details and
permission pages show the exact sealed digest, activation says unsigned and
does not claim publisher identity, version rows distinguish digest prefixes,
and grant confirmation states that consent is digest-bound. Remaining closure
requires a bounded source/transferred-digest receipt plus capability deltas.
Signed fixtures must cover
valid, unknown, wrong-namespace, modified, expired, rotated, revoked, downgrade,
and offline cases. An end-to-end release must demonstrate that a compromised or
replacement package cannot inherit enablement, consent, configuration secrets,
or update authority merely by reusing the manifest publisher and package ID.

### EQ-013 — P1 — Aggregate worker residency needs a verified process-lease and refusal contract

**Status: Architecturally implemented with focused direct coverage in current
HEAD; retained execution evidence, an actionable controller
refusal path, critical-work leases, and ecosystem-scale proof remain open.**

**Implementation evidence.** The new `WorkerResidencyBudget` owns one locked,
reference-identity reservation table. Application workers default to a maximum
of eight registrations and 512 MiB summed from their declared per-Job limits;
bounded bridge arguments may configure 1–256 workers and 16–16,384 MiB. One
exact trusted Settings identity bypasses the application ceiling but is
separately limited and reported as a single control-plane reservation, making
the total default envelope application budget plus at most one 16–256 MiB
Settings Job.

The runtime now owns reservation lifetime rather than the bridge sampling
process state. `WidgetProcessOptions.ProcessLeaseFactory` is invoked inside the
serialized lifecycle gate immediately before session resource creation. The
returned lease is released by `OnProcessExited` only after actual process exit,
or exchanged once and disposed by `DisposeSessionAsync` after pipe, process,
and Job detachment. This no longer treats a disconnected pipe as released
capacity and makes the exit/disposal race idempotent. Admission failure has a
distinct exception and cleans up the inert session. Aggregate totals appear in
bridge diagnostics. The controller scaffold and Clock sample now select five-
minute idle unload.

Focused tests prove concurrent N+1 count admission, declared-memory refusal,
bounded option parsing, normal-crash release, request-timeout release,
idle-unload release and reacquisition, and access to Settings under exhausted
application capacity. Direct runtime cases prove that admission denial remains
pre-launch and does not publish a worker failure, then inject a lease and prove
one acquisition/release for a natural crash, restart, and cooperative stop.
Clean result `20260809T141527Z-8946c731` retains 36/36 runtime and 40/40 bridge
cases. Their JUnit names explicitly include pre-launch admission refusal, exact
process-session lease ownership, race-safe count admission, declared-memory
accounting, Settings access, suspend-when-hidden, and idle-unload behavior. The
unlisted fault paths and native refusal UX below remain open despite that green
coverage.

The refusal is not yet a usable UI contract. `WidgetBridgeServer.RunAsync`
maps every non-cancellation request exception, including
`WidgetProcessAdmissionException`, to `BridgeError("request_failed", message)`.
The native `SafeBridgeError` discards even that generic code and retains only a
bounded message. `SyncWidgetActivity` writes lifecycle failure to the diagnostic
log; `RefreshWidgetSnapshot` stores a four-second `lastActionMessage_`, but
`DrawWidgetFooter` shows that message only while focus is in the tray. An open
widget with no snapshot therefore continues rendering `Starting isolated ...`
rather than a persistent capacity state. Settings keeps the control plane
reachable and shows aggregate worker/memory totals in the Bridge summary, but
its worker section renders names only for recorded failures. Because admission
denial is deliberately not a worker failure, it identifies neither the denied
widget nor the resident owners. Installed community widgets can be disabled on
a separate details page; built-in widgets explicitly cannot, and neither route
is connected to the capacity refusal.

EQ-020 makes the same missing ownership a liveness problem, not only a
rendering problem. The native bridge client performs synchronous, untimed pipe
reads on `OverlayApp` paths and cannot consume the managed bridge's new
concurrent-request capability. If package authority or startup stalls, the host
cannot transition to a typed refusal state or send an unrelated recovery request
because its UI thread is blocked. Bridge I/O and request deadlines therefore
need to move behind the coordinator before persistent failure UI can be
considered complete.

The same missing state owner appears after a successful render. On a later
`GetSnapshot` failure, `OverlayApp::RefreshWidgetSnapshot` reports transient copy
but does not invalidate the cached snapshot. Preserving last-good presentation
can be useful, but without an explicit stale/unavailable state it leaves controls
looking live and allows follow-up input to fail through the same untyped path.
The coordinator should make last-good retention a deliberate state transition,
disable or qualify stale actions, retain a safe typed reason, and expose one
controller-reachable retry or remediation action.

DLV-016 improves the retained performance evidence without pretending to prove
the aggregate ecosystem contract. Five fresh Release processes repeat one
stable native 55-node/48-semantic-node workload through 256 updates, with
projection p95 `0.399–0.591 ms`, private working set `0.63–1.32 MiB`, exact
source/executable hashes, and bounded variation. A separate current real-host
observation records Hidden/Visible CPU p95 `0.09737%/0.09773%`, zero hidden
host timer messages, and zero post-warmup Direct2D frames. The runs remain dirty
focused local evidence; they do not cover GPU/DWM, scheduler wakeups,
controller hardware, game-frame impact, long-run trends, or multi-widget
accumulation. DLV-011 must use these as a comparison baseline, not extrapolate
them into a pinned-surface or Community-ecosystem budget.

DLV-011 now supplies that bounded comparison. Five focused Release processes
measured a real host-owned top-level tool window plus a two-node production UI
Automation projection at `0.684-0.707 MiB` incremental private working set, 0%
normalized 750 ms idle CPU, and `0.0003-0.0005 ms` semantic p95. The selected
Win32 candidate keeps HWND, focus, topmost, teardown, placement, and semantic
authority in the host and accepts only a data descriptor. This closes the
architecture-choice gate, not the product or compatibility gate: the fixture
does not prove cross-process click delivery over a game, compositor/GPU cost,
protected media, physical DPI/hot-plug behavior, or an independently surviving
YouTube surface. Those claims must remain attached to later generic-pinning and
rich-media milestones rather than being inferred from the small harness.

**Why it matters.** A user exploring community widgets can accumulate resident
.NET processes during a gaming session even when every individual package
obeys its manifest and Job limit. The resulting memory, scheduler, handle, and
background-service cost grows with widgets used rather than widgets visible.
Per-widget containment prevents one process tree from escaping its ceiling; it
does not protect the game's system-wide resource headroom or make the product's
“lightweight” promise true at ecosystem scale.

**Underlying problem.** Admission and exact process-session ownership now meet
in the host-owned supervisor/runtime boundary, but typed admission reason,
presentation state, resource attribution, and recovery action do not cross that
boundary together. Measured usage, per-widget user remediation, preference,
and temporary critical work therefore remain disconnected pieces rather than a
complete product contract.

**Recommended direction.** Preserve the explicit conservative count, summed
Job-limit admission, and process-session lease. Extend direct fault injection
across the remaining terminal paths. When capacity is unavailable,
reclaim only explicitly unload-eligible least-recent workers, or retain the
current refusal and let Settings explain which resident widgets own the budget.
Give admission failures a stable safe protocol code and bounded structured
details (count versus declared-memory pressure, current/maximum totals, and the
requested widget), not a message that native code must parse. The host should
render a persistent error state with Retry and Open resource management actions.
Settings should attribute reservations to sanitized widget names, declared Job
limits, residency policy, and unload/disable eligibility, then release or
disable only through the supervisor/catalog owners. Never silently reinterpret
`keep-alive`.

Make the least-cost authoring path explicit as well. Ordinary scaffolded
widgets should use a bounded idle-unload policy unless they declare and explain
a genuine Background continuity requirement. Settings should expose measured
CPU, working/private memory, wakeups, crash count, and network activity plus a
user-controlled residency override where safe. Spotify's long authorization
flow should eventually use a bounded, visible critical-work lease or a broker-
owned continuation rather than making the process permanently resident to
protect one temporary operation.

**Tradeoff.** Aggressive unloading increases cold-start latency, discards
unpersisted in-memory state, and can break legitimate explicitly retained
work. A hard global cap can also deny a foreground launch if every resident is
pinned. That is preferable to hidden overcommit only when the UI makes the
choice actionable. Cached snapshots, durable private state, bounded temporary
leases, author-declared idle eligibility, and user pinning/overrides provide
more predictable control than memory-pressure eviction.

**Resolution evidence.** The clean all-lane bundle retains Release
`WidgetRuntime.Tests` at 36/36 and `WidgetBridge.Tests` at 40/40. Extend the
current supervisor/runtime tests with
invalid-snapshot, failed-connection, companion-
failure, disable/remove, restart, catalog-replacement, cancellation, and bridge-
shutdown accounting. Delay process exit in failure fixtures so release cannot
accidentally pass through timing. Prove each session reserves/releases exactly
once, Settings remains reachable, and `keep-alive` is never silently evicted.
Add a native contract test for opening a widget against a full count and memory
budget: the panel must leave its starting state, preserve a typed reason, offer
a controller-reachable recovery path, and retry successfully after an eligible
owner is released. Add Settings rendering/action tests that attribute every
reservation, distinguish non-reclaimable owners, and do not rely on a denied
launch appearing in the worker-failure list.
Retained clean
measurements should compare 1, 8, and a higher bounded number of mixed-policy
widgets across launch, Background, reopen, and long churn, including aggregate
private working set, CPU, wakeups, handles, threads, GPU/game-frame impact, and
latency. The controller performance surface and user override require packaged
visual and interaction evidence.

### EQ-015 — P1 — Local packaging works, but published SDK governance remains open

**Status: Partially implemented in current HEAD. DLV-010 closes the cloneable
offline scaffold, generated snapshot/test, and source-to-package journey;
externally published/versioned SDK governance and transactional template input
remain open.**

**Accepted DLV-010 reassessment.** Commit `83cc32d`, integrated by `e68b8be`,
replaces the checkout `ProjectReference`/`--sdk-project` path with a matching
content-addressed `GameBarAlternative.WidgetSdk` package in the generated
project's relative `.gbar/packages` feed. The generated executable test drives
Created/Interactive/Background lifecycle, state-changing action, retained state,
and the exact snapshot consumed by render/replay. Source-aware `gbar pack` now
reuses the bounded dev-generation builder with generation-private intermediates,
omits symbols/runtime-owned SDK contract assemblies, validates the staged
generation, and atomically publishes through the existing deterministic packer;
raw staged-directory packing remains unchanged.

The external temporary-directory fixture executes offline scaffold, Release
build, generated test, validate, render, replay, byte-identical directory/project
source packs, checkout-path inspection, two-version install, selection,
rollback, disable, and removal without generated-file edits. Retained focused
run `20260810T123758Z-6ca1bb73` passes CLI/scaffold 53/53, catalog 35/35, and 52
documentation contracts in 45.536 seconds with stable dirty-worktree provenance.
This is sufficient assignment evidence, not clean product-release evidence.

**Pre-DLV-010 evidence.** `NewCommand` resolved the source checkout or required an
explicit existing non-reparse `WidgetSdk.csproj` through `--sdk-project`. It
does so before creating the output directory, escapes the relative MSBuild
path, and fails with an actionable message rather than emitting the unpublished
`GameBarAlternative.WidgetSdk` placeholder package. The help, CLI README,
quickstart, authoring, publishing, and troubleshooting guides state the same
temporary local-project contract.

That failure-before-write guarantee is currently narrow. After SDK and identity
preflight, `NewCommand` creates the final target and streams each recursively
enumerated template file through global string replacement. It does not stage a
complete sibling tree, pre-read/validate the full input, or remove output if a
later template read or destination write fails. `TemplateLocator` accepts
`GBAR_TEMPLATE_ROOT` when it merely contains `template.json`; `NewCommand`
neither deserializes the declared `templateVersion` nor enforces a closed file
inventory, file/count/byte bounds, reparse-safe traversal, or text-versus-binary
mode. A stale template can therefore be silently interpreted by a newer CLI,
an unexpected editor/backup file is copied into every project, and a future PNG
or other binary starter asset would be corrupted by `ReadAllTextAsync` and
`WriteAllTextAsync`. Existing tests cover invalid identity, missing SDK, token
replacement, validation, and one external Release build; none injects a
mid-generation failure, unsupported template version, extra file, binary file,
or reparse traversal and asserts atomic cleanup.

The pre-DLV-010 HEAD corrected two immediate template defects. The generated
README used `gbar dev` for executable integration and accurately said
`gbar render` consumed snapshot JSON only. The manifest and Clock sample switched
from permanent `keep-alive` to `unload-after-idle` at 300 seconds, and the CLI
test asserted that residency metadata. A new test copied the template under an
unrelated temporary root, proved unresolved SDK discovery left no output
directory, supplied the exact SDK project, and successfully ran a bounded
Release build of the generated widget. However, the README told authors to add
a typed-fake snapshot exporter while the template contained no test project,
exporter, or static snapshot, so its subsequent `gbar replay` path had no
generated input.

At that baseline, the package path was independently incomplete. The generated manifest declared
`payload/<WidgetName>.dll`, but its README runs ordinary `dotnet build`, whose
output remains under `bin`, followed by `gbar validate .`. `Validation.cs`
validates manifest and GBSS syntax without checking that the manifest entrypoint
exists. `gbar dev` then appears to make the source valid because
`DevGenerationBuilder` deliberately builds into a private temporary package
root at the declared payload path. That generation is not exposed for release.
`WidgetPackagePacker` correctly requires the declared entrypoint and therefore
rejects `gbar pack <generated-source>` as `missing_entrypoint`. The generated
README contains no publish/staging/pack command, while the CLI README advertises
that exact source-directory pack command. The focused scaffold test stops after
`dotnet build`; it does not execute validate, dev, snapshot export/replay, pack,
inspect, install, or enable from the generated project.
`docs/plugin-platform.md` also overstates the current starter as scaffolding a
“test/replay” workflow. The template contains `replays/smoke.json`, but no test
project, snapshot exporter, or static snapshot to feed it. This is public
contract drift, not just missing polish: the platform overview promises a
workflow its canonical generated artifact cannot execute.

The dependency itself is also not ready to be governed as a public platform
contract. `WidgetSdk.csproj` has target-framework, nullable, implicit-using, and
warnings-as-errors settings plus a source `ProjectReference` to
`WidgetProtocol`; it has no package identity/version/description/repository
metadata, generated package/documentation settings, package validation, or
checked-in API-compatibility baseline. A textual inventory finds about 201
public class/record/interface/enum/struct declaration lines in `src/WidgetSdk`.
That count is not a quality score, but it makes accidental pre-release API
expansion and later breaking changes expensive unless the supported surface is
deliberately versioned before publication.

**Why it mattered at the pre-DLV-010 baseline.** The misleading successful-but-unbuildable scaffold was
removed. Standalone GitHub repositories—the intended sharing unit—still cannot
consume the SDK through a supported published dependency, and developers must
bring a platform checkout, invent a snapshot-export path, and discover a second
undocumented build/staging procedure before they can create a package. The
apparently successful validation is particularly misleading because the first
complete package validation occurs only after the author reaches `gbar pack`.
That remains short of the promised 15-minute community starter experience.

**Remaining underlying problem.** The accepted local dependency and package
journey do not make the SDK an externally published platform contract. DLV-046
has closed the versioned transactional-template gap; DLV-047/DLV-050 have closed
the local release-unit/API-baseline and mixed-runner gap; and DLV-048 has closed
the canonical quickstart drift for the marked offline journey. What remains is
external immutable publication with provenance/support policy, ordinary external
CI against that published unit, and isolated executable semantic preview—not a
repository-wide test migration or another local packaging path.

**Recommended direction.** Treat the CLI, template, SDK/runtime packages, and
compatibility range as one release set. The production endpoint is a supported,
immutable NuGet SDK/runtime release plus a template that pins a compatible
version and can be restored from a clean machine without the platform source.
The accepted content-addressed local package is appropriate for offline
scaffolding until that artifact exists; do not reintroduce checkout references.
Preserve the accepted checked-in API baseline and intentional pre-release reset
workflow. Before external publication, add the remaining package validation,
XML documentation, symbol/source metadata, provenance, and support policy
without retaining obsolete local APIs solely for compatibility.

Retain DLV-010's generated `WidgetTestHost`/`SnapshotJson` executable and its
small complete build-test-render-replay-package loop. The remaining isolated
scenario worker should build on that public fixture contract rather than
replacing it with another author-visible result protocol.

Make template generation transactional and versioned. Parse a strict template
manifest before touching the destination; bind its supported schema/version to
the CLI release and list each expected relative file with text/binary mode and,
for packaged templates, a content digest. Apply count, per-file, aggregate-byte,
path, and reparse bounds. Materialize and validate the complete replacement set
in a unique sibling staging directory, then publish it by one rename only when
the requested target does not exist; on any failure, remove only that verified
staging directory. If contributor overrides remain supported, run them through
the same validation while clearly treating their content as local developer
input. Do not recursively copy arbitrary files merely because they sit beside
`template.json`.

Retain DLV-010's explicit source-aware `gbar pack` operation and its separation
from low-level staged-directory packing. It already owns the bounded build-to-
generation contract, validates the declared entrypoint, and publishes only the
accepted immutable generation. Future release CI should call this same command
rather than grow another MSBuild or staging path.

**Tradeoff.** Publishing an external SDK creates versioning, symbol/source,
provenance, and support obligations. DLV-010's content-addressed local dependency
is a transparent offline bootstrap and avoids checkout coupling, but it is not
an upgrade/distribution channel and should eventually yield to the governed
external SDK release.
Reusing the dev generation builder reduces drift, but release packaging must
exclude dev readiness files/catalog state and must not launch the overlay;
duplicating build/staging logic would make development and release artifacts
diverge again. A closed template manifest is slightly more maintenance than
directory enumeration, but it makes binary assets, compatibility, provenance,
and review diffs explicit. Requiring a nonexistent destination simplifies an
atomic rename; supporting an already-created empty directory would require a
more complex recoverable publication contract with little author value.

**Remaining resolution evidence.** Publish the CLI/template/SDK as one immutable
versioned release unit and exercise the same generated commands from that
packaged distribution in ordinary external CI, including isolated generic-worker
launch. Check the intended public SDK surface against a committed compatibility
baseline and require an intentional API/host-range/version update for breaking
changes. Parse a strict template manifest with supported version, closed file
inventory, text/binary mode, digests, path/reparse/count/byte bounds, and stage
the complete generated tree before one atomic publication. Inject unsupported
template versions, unreadable inputs, destination faults, unexpected/binary
files, size/count overflow, and reparse traversal; every failure must preserve
the requested target contract and clean only its verified staging tree.

### EQ-002 — P1 — Responsive focus identity required an explicit contract

**Status: Implemented in current HEAD; managed and native coverage is retained
in the clean all-lane bundle.**

**Implementation.** Protocol v13 adds an optional bounded
`focusPersistenceId` only to focusable nodes. SDK focusable elements expose
`PersistFocusAs`, and `UI.NavigationShell` generates one stable key per logical
destination for its distinct compact and rail controls. The native snapshot
parser carries the field, while transition-owned reconciliation on `WM_SIZE`
and presentation/snapshot state changes matches only one explicitly opted-in
target in the same input scope, outside paint. Omitted keys,
shared action IDs, ambiguous keys, and cross-scope candidates fail closed.
`RenderResult.focusActionIds` and action-based focus inference were removed.

**Compatibility and rendering implications.** Snapshots without the field keep
their prior required protocol version and exact-ID/tree-order fallback.
Authoring the field negotiates v13. Responsive equivalence is resolved from the
immutable snapshot and logical surface mode before drawing, so the first frame
receives the corrected focus ID; geometry-only clipping recovery remains
post-layout. The field never participates in action dispatch.

Disabled and busy nodes remain valid focus candidates by platform contract;
their activation is unavailable, but focus is retained so the user can inspect
their label and state cue. EQ-009 records the source/test audit that corrected
the contrary assumption.

**Resolution evidence.** Public focus guidance now treats action routing and
focus persistence as separate contracts. The changed managed tests cover
distinct destinations sharing one action without sharing persistence,
compact/expanded preservation, v13 negotiation, and legacy omission. Native
tests cover ambiguous-key rejection and action-only non-equivalence. The
clean result `20260809T141527Z-8946c731` retains 84/84 managed Widget SDK cases,
including named focus-persistence/navigation-shell cases. Its native build log
retains 41 Focus Navigation checks, 20 Widget Surface Focus checks, passing
`WidgetBridgeCatalogTests`, 4,632 Declarative Renderer checks, and the OverlayHost
Release build. Source inspection and the retained clean run now support the
intended automated contracts; physical mixed-DPI/controller behavior remains a
separate manual gate.

### EQ-003 — P1 — `OverlayApp` is a central ownership and change-risk hotspot

**Status: Open; the first extraction boundary is now identified precisely.**

**Evidence.** `src/OverlayHost/main.cpp` is 4,043 lines, and `OverlayApp` spans
about 3,753 of them. A mechanical inventory finds roughly 80 method declarations
and a 118-line member-state region. The class owns development
arguments/readiness, performance records, both HWNDs and their message paths,
GameInput/XInput, foreground ownership, catalog revision retries, bridge
descriptors and snapshots, runtime/presentation generations, widget lifecycle,
display/DPI refresh, presentation transitions, pointer/controller routing,
focus/slider/pressed state, renderer cache invalidation, D2D/DWrite resources,
shell/widget drawing, and shutdown.

The coupling is visible in a few concrete paths. The approximately 300-line
`HandleMessage` timer branch advances visual transitions, polls controllers,
pumps bridge events, reconciles appearance/catalog revisions, invalidates
widget snapshots, gates host effects by runtime generation, and closes the
overlay. `RefreshWidgetCatalog` combines bridge startup/I/O, descriptor diffing,
focus/scroll/slider eviction, persistent available-widget reconciliation,
lifecycle synchronization, and content-reveal animation. `RefreshWidgetSnapshot`
and `DispatchWidgetAction` combine transport, instance/sequence authority,
focus/pressed state, user messages, repaint scheduling, and navigation fallback.

The existing abstractions stop one layer too low. `WidgetBridgeClient` owns
process/pipe transport, parsing, bounded event queues, and catalog revision
tracking; `WidgetLifecycle` purely computes a desired lifecycle target. Their
focused tests cover parsing, queues, generation diffs, and lifecycle mapping.
No directly testable owner composes those contracts into the host's catalog,
snapshot, lifecycle, retry, and failure state. Searches find no direct native
tests for `RefreshWidgetCatalog`, `SyncWidgetActivity`, or
`RefreshWidgetSnapshot`; those behaviors are exercised only through the whole
window/smoke path.

**Why it matters.** The problem is not line count by itself; it is the number of
independent state machines sharing one mutable owner. Changes to input,
lifecycle, catalog, presentation, or rendering can invalidate assumptions in
another area and are difficult to test without the complete window. This raises
review cost, encourages more fields and helper methods in the same class, and
makes senior-level ownership boundaries hard to see.

**Underlying problem.** Algorithms and transport mechanics have been extracted,
but host-side widget session ownership has not. The Win32 application object is
simultaneously the bridge supervisor, catalog reconciler, lifecycle state
machine, snapshot cache, input authority, presentation coordinator, and error
surface. This also explains why capacity refusal and startup/protocol failures
collapse into transient `lastActionMessage_` text rather than durable typed
per-widget state.

**Recommended direction.** Extract one `WidgetSessionCoordinator` above
`WidgetBridgeClient`; do not begin with a broad UI/controller rewrite. It should
own the bridge client, descriptor/snapshot collections, catalog retry state,
tracked lifecycle target, controller input sequence, and typed per-widget
session status. Its inputs should be a small host-state projection
(`surface`, focus region, selected/active widget) plus explicit commands such as
reconcile catalog, refresh/restart, send input, and pump events. Its outputs
should be a bounded typed result batch: available IDs, snapshot/status changes,
runtime or presentation replacement, lifecycle result, host effect, and retry
request.

`OverlayApp` should remain the Win32/presentation adapter. It applies those
results by clearing focus/renderer state, saving persistent selection,
requesting a reveal, invalidating the HWND, or dispatching a host command.
Focus memory, slider/pressed interaction, D2D resources, and transition timing
should stay outside the first extraction. The coordinator must not receive an
HWND, renderer, or references to arbitrary `OverlayApp` fields, and it should
not introduce a generic event bus. Later display-environment or development-
readiness extractions should be justified independently.

**Tradeoff.** This adds an orchestration layer above an already substantial
transport client. The value comes only if it owns the mutable session state and
returns domain results; a facade that forwards every bridge call or accepts
callbacks for window/render/focus operations would add indirection without an
ownership boundary. Keeping focus and rendering outside initially leaves some
coordination in `OverlayApp`, but makes the first migration reviewable and
avoids a speculative host framework.

**Resolution evidence.** Add deterministic coordinator tests for runtime versus
presentation-only replacement, removed active/hovered widgets, last-good
catalog retry/abandon, stale invalidations and host effects, failed start and
snapshot/protocol responses, exact lifecycle transitions without background
relaunch, restart, and shutdown. Prove each transition emits one typed result
batch and leaves no duplicated descriptor/snapshot/lifecycle fields in
`OverlayApp`. A capacity denial or worker failure must persist as an owned
widget status with retry/resource-management actions instead of expiring footer
text. Finally, show that the next bridge lifecycle/catalog feature changes the
coordinator and focused tests without editing unrelated drawing or controller
polling regions of `main.cpp`; reduced line count alone is not closure.

### EQ-031 — P1 — Text-entry commit retains stale native authority across a nested loop

**Status: Closed by accepted DLV-079 commit `d702d37`, integrated through
`d116f0d`.**

**Evidence.** DLV-075's `OverlayApp::OpenTextEntryModal` resolves a
`WidgetNode` through a `const WidgetSnapshot&` stored in `widgetSnapshots_`,
then passes control to `TextEntryModal::Show`. `Show` owns a nested
`GetMessageW` loop. The owner continues processing timer and bridge events
during that loop, including paths that call `RefreshWidgetSnapshot` and
`insert_or_assign` the map value or erase/clear snapshot entries. When `Show`
returns a value, `OpenTextEntryModal` dereferences the pre-modal node and sends
its action using the pre-modal input scope. A worker snapshot refresh,
replacement, removal, hide, or active-widget change can therefore leave both an
invalid reference and stale action authority at the commit boundary.

The same candidate does not yet meet its controller/responsive acceptance
criteria. Left/Up both move one position backward and Right/Down both move one
position forward through a single linear target list, so the keyboard is not a
spatial controller surface. Its fixed 760 by 520 logical window is DPI-scaled
and owner-centered but not bounded or reflowed to the monitor work area; at
150% the requested 1140 by 780 pixels can exceed a 720-pixel work area. Existing
native evidence proves an edit UIA provider inside an 800 by 600 owner, not
spatial direction semantics or on-screen bounds at compact/standard/150%.

**Why it matters.** The public boundary correctly withholds raw keyboard events
and HWND authority from widgets, but the final committed action still needs
fresh native authorization. Holding container-backed references across a
reentrant Windows message loop is unsafe even when the modal normally completes
quickly. The navigation/layout gaps also turn an ostensibly controller-first
feature into a sequence that is difficult to discover and can be clipped on a
real small or scaled display.

**Required correction.** DLV-079 must capture only immutable bounded request
values before opening the modal. After commit it must re-resolve the current
active widget, admitted snapshot, source node/action identity, generation,
enabled state, and active input scope, sending exactly once only when all still
match; every replacement/removal/hide/disable/stale case must fail closed. Add
deterministic mutations while the modal is open. Give the keyboard and action
row true spatial 2D controller links, bound/reflow the modal to the active work
area at compact, standard, and 150%, and retain accurate UIA names, roles,
focus restoration, and visible bounds. Do not expand widget key/HWND authority,
redesign the public query contract, or use screenshot capture as acceptance.

**Resolution.** `d702d37` introduces one value-only admission seam. The host
copies the bounded widget/runtime/snapshot/scope/node/action/text request before
the nested loop and never dereferences the pre-modal snapshot or node afterward.
Commit obtains the current descriptor and snapshot and admits one send only when
the active interactive widget, runtime generation, snapshot sequence, input
scope, TextEntry node, enabled/non-busy state, and exact action ID still match.
Focused deterministic cases cover unchanged current authority, hide, active-
widget and runtime replacement, snapshot refresh, scope change, removal,
disablement, and action replacement. The modal now derives one DPI-aware layout
inside the active monitor work area, exposes all edit/key/action controls through
native UI Automation providers, and uses spatial non-wrapping directional
selection across keyboard and action rows. The direct modal/admission fixture,
native bridge parser, production OverlayHost Release build, and 55 documentation
contracts pass. The corrected prefix is integrated through `d116f0d`.

### EQ-032 — P1 — Launcher retained organization violates catalog-page composition

**Status: Closed by DLV-080 `2e38f00` plus DLV-081 `b2596e1`, integrated
through `d116f0d`.**

**Evidence.** DLV-076 correctly persists at most 32 recent opaque SavedIds and
uses that exact set for the provider-backed Recent: Only filter. In Recent:
First mode, `EffectiveQueryLocked` does not change the ordinary catalog query.
`GameLauncherPresentation.Render` then orders only `snapshot.Items`, the current
bounded provider page, against the recent map. A game launched from page 3 can
be recorded durably, but after restart the first A-Z provider page does not
contain it and the game cannot be rendered near the front. The new 40-item test
launches recent identities that are already present in one loaded snapshot, so
it cannot detect the page-boundary failure.

DLV-077 introduces the inverse composition error. `LoadPageAsync` receives as
many as 64 provider rows, removes only manual identities already present in that
Game query, appends up to 32 separately resolved manual rows, and returns the
combined list through the same `WidgetCursorPage`. The shared resource's
`Normalize` method explicitly rejects `page.Items.Count > PageSize`; Game
Launcher configures `PageSize` to the provider maximum of 64. A full page plus
one manually added Application can therefore fail the collection even though
both inputs are individually valid and bounded. Its focused direct cases use
small catalogs, while the installed fixture proves discovery/action ordering
but not a successful full-page Library render after the add.

**Why it matters.** The visible control says Recent: First and the delivery
requirement is complete-library organization. Per-page sorting changes order
again at every cursor boundary and makes persisted history appear ineffective
for exactly the large libraries that require paging. Loading the whole catalog
to compensate would regress the bounded-query architecture.

**Required correction.** DLV-080 should compose bounded retained recent and
resolved manual display sections outside the independently paged provider
resource, deduplicate by exact SavedId, and route activation only through
current ResolveSaved launch revalidation. The cursor loader must return no more
than the exact requested page count under a maximum 64-row page plus maximum
manual/recent state. Preserve normal catalog cursor behavior, filters/sorts,
focus, manual membership, favorites, groups, and bounded CAS state; automatic
Games should be presented as already included instead of receiving a no-op
manual membership toggle. Evidence must combine a later-page recent cold
restart with a full provider page plus manual entries, cross-section
deduplication, and missing/replacement rejection. No public provider sort,
full-library load, or process observation is warranted.

**Candidate review.** `2e38f00` corrects the two structural errors: cursor
loaders return only provider rows, fixed recent/manual slices are separately
bounded and deduplicated, automatic Games cannot retain manual membership, and
fixed-row launches revalidate exact SavedId authority. One live transition
remains. Recording a successful launch while Recent: First is already active
updates organization but not the separately resolved fixed slice. Presentation
promotes the current-page ID, fails to consult that same current provider item,
and falls back to stored display projection, which disables the tile until a
query reload. DLV-081 must use the exact current page item first while preserving
the resolved-fixed then display-only fallback once the item is no longer
retained. DLV-081 `b2596e1` implements that ordering, preserves current
display/source/artwork and enabled state without reloading, deduplicates the
promoted identity, selects a remaining current catalog anchor, and revalidates
the second launch by exact SavedId. The focused Game Launcher suite passes
36/36; the corrected prefix is integrated through `d116f0d`.

### EQ-033 — P1 — Protected Wi-Fi secret and rollback ownership are not terminal

**Status: Closed by DLV-093 `3ce8991`, integrated with the corrected DLV-087
prefix through `63ca3a2`.**

**Resolution.** The host now overwrites the password EDIT control before
destruction and sends only bounded metadata in JSON; the credential itself uses
a dedicated mutable native byte frame and managed mutable owner that are zeroed
before reply publication and on rejection, cancellation, I/O failure, and
exception paths. Provider command copies and the profile XML/PInvoke owner are
also cleared, and tests compare derived sentinels rather than storing a raw
password string. Native Wi-Fi creates a random per-attempt profile name, stores
a 32-byte ownership token as per-profile custom user data, and funnels every
failure/timeout/disposal delete through an exact interface/profile/token match.
Mismatch, unavailable verification, and delete failure are explicit results and
never delete current Windows state. Focused evidence passes provider 55/55,
bridge 76/76, Network Controls 22/22, AppContainer 6/6, both native secret
targets, and docs 55. The one clean checkpoint stopped later at an unrelated
accepted Game Launcher fixture after the changed groups passed and was not
repeated.

**Evidence.** DLV-087 correctly keeps the credential out of the ordinary widget
worker, snapshots, public capability calls, logs, and persisted overlay state.
It does not keep the credential inside zeroizable owners. The native client
creates a WinRT JSON string, stringifies the envelope, and converts it to an
ordinary UTF-8 `std::string`. The managed `BridgeFrameChannel` allocates a
request `byte[]`, deserializes a retained `JsonElement`, and never clears that
frame. `BridgeRequestClassifier` and `WidgetBridgeServer` each deserialize a
`BridgeProtectedWifiRequest`, so the same secret becomes two immutable managed
strings before the later mutable copies are cleared. The masked EDIT control's
text is not overwritten before destruction. Tests assert later `char[]` and XML
clearing but do not observe these actual transport owners.

The Native Wi-Fi half records a protected pending connection as native key,
interface GUID, SSID-derived profile name, and `CreatedProfile = true`.
Failure, timeout, and terminal cleanup delete whatever current profile has that
name. `WlanSetProfile(..., bOverwrite: false)` prevents an existing profile from
being overwritten at creation time; it does not establish ownership of a later
profile still stored under the same name. The direct rollback fixture tests
pre-existing rejection and ordinary success/failure, but not replacement
between creation and asynchronous terminal handling.

**Why it matters.** Password collection creates a stronger obligation than an
ordinary authenticated bridge message: process memory and crash artifacts
should not retain avoidable plaintext copies after the operation. Rollback is a
data-loss boundary. Deleting by a common SSID-derived name after an unbounded
external mutation window can remove a valid Windows profile created or replaced
by another actor.

**Required correction.** DLV-093 must use one bounded mutable transport payload
whose native serialization, pipe buffer, managed parsing, command, P/Invoke,
and XML owners are explicitly cleared on every terminal path, without duplicate
full-request deserialization or raw-password test strings. Clear the EDIT text
before window destruction. Give each created profile an attempt-unique identity,
retain exact interface/profile/generation ownership, conditionally verify it
before deletion where supported, and report rollback failure rather than
claiming clean failure. Cover creation/replacement/delete races and run one
production-shaped route plus one exact-commit checkpoint. Do not expand into
pipe encryption, credential persistence, additional Wi-Fi modes, or broad
bridge refactoring without a new demonstrated requirement.

### EQ-004 — P1 — Immutable hosted execution evidence remains

**Status: Implemented in commit `4450cfa`; bounded local/CI
orchestration, gated Job ownership, capped output/case extraction and pump
completion, honest clean-evidence eligibility, exact selected native-toolchain
provenance, SHA-pinned actions, documented retention, and individually bounded
Community-artifact provenance share the aggregate deadline. Package root,
per-file, aggregate-byte, traversal-entry, reduced-budget, and exhaustion edges
have focused tests. A clean release-eligible all-lane local result is retained;
immutable hosted execution remains open.**

**Evidence.** The repository has strong `Directory.Build.props` defaults. All
33 managed test projects are executable projects with custom `Program.cs`
harnesses; none references `Microsoft.NET.Test.Sdk`.

Current HEAD replaces the repeated `Verify.ps1` command
block with a checked-in 41-step `verification-steps.json` manifest and a narrow
`VerificationRunner.psm1`. The script validates stable IDs and lanes, supports
managed/native and selected-step runs, enforces a default 1,800-second aggregate
budget plus step-specific deadlines, emits per-step JUnit/logs, and writes an
aggregate JSON result. Provenance includes commit, dirty flag, OS/architecture,
PowerShell/.NET versions, manifest digest, and up to 256 Community package
digests. A capped C# stream pump drains stdout/stderr after truncation, defaults
each stream to 4 MiB, and reports retained bytes and truncation. JUnit extraction
streams lines, truncates a line at 8,192 characters, caps cases at 10,000, adds a
truncation failure, and cannot report green when the process exit failed.
`Test-VerificationRunner.ps1` checks all managed test projects are represented
and exercises success, exit-code preservation, high output, normal timed and
detached descendant trees, bounded case extraction, JUnit conversion, and the
failed-process/printed-PASS case.
That is a material architectural improvement and avoids forcing an immediate
test-framework migration. The worktree also includes the previously omitted
WidgetTicker suite, caps post-timeout termination wait at five seconds, labels
its dirty fingerprint `dirtyStatusSha256`, sets `releaseEvidenceEligible` only
for a clean tree, and adds a Windows GitHub Actions workflow with separate
managed/native jobs, 35-minute job ceilings, always-uploaded artifacts, and
30-day retention.

The final follow-up closes the process-tree gap without duplicating the native
suspended-process launcher. A repository-owned PowerShell launcher blocks on a
named event; the runner starts it, assigns it to a kill-on-close Job, starts the
capped pumps, and only then signals it to invoke the real command. Actual test
code therefore cannot execute before containment. On every root exit or timeout,
Job closure terminates inherited descendants before pump completion is awaited;
the combined pumps have a separate five-second deadline. Command-request files
are capped at 256 KiB and the checked-in manifest is capped at 256 KiB, 128
steps, 32 arguments per step, and bounded field lengths.

Community package provenance is now separately bounded. The helper runs through
`Invoke-BoundedVerificationProcess` with a 30-second process-tree deadline,
limits traversal to 4,096 entries and 256 archives, limits each archive to 72 MiB
and their total to 2 GiB, shares the 4 MiB output cap, opens one restrictively
shared stream for length/hash, and skips enumerated reparse points. Its focused
self-test proves one exact four-byte digest plus per-file, aggregate-byte,
traversal-entry, and root-junction rejection.

The shared deadline is now applied across the complete subprocess preflight.
`Get-RemainingVerificationTimeout` computes the smaller of the aggregate
remainder and each
local ceiling before Git status/commit, .NET, package, native-toolchain, and test-
step launches. Native discovery and compiler/manifest-tool hashing moved into a
ten-second Job-backed helper with a 128 MiB per-tool ceiling. The package helper
also requires its root below the evidence root and rejects a root reparse point.
The self-test proves a partially consumed 60-second budget reduces a 30-second
local ceiling to 14 seconds and proves typed failure before launch once the
aggregate is exhausted.

Local artifact cleanup is explicitly manual so review evidence is not deleted
implicitly; CI retention is 30 days. Native provenance records the exact
selected Visual Studio installation and MSVC version, compiler version/hash,
the separately selected OverlayHost/InputProbe Windows SDK versions, and the
manifest-tool version/hash. It also records GitHub runner-image identifiers when
available. Workflow actions are pinned to immutable commit SHAs. Retained clean
result `20260809T141527Z-8946c731` passed all 41 steps and 755 JUnit cases with
zero failures, errors, or skips in 313.016 seconds for exact commit `dc6b092`.
Its clean status and `releaseEvidenceEligible: true` close the local full-run
requirement; it retains 29 package digests plus the exact toolchain fields. The
committed workflow has not produced a referenced immutable run associated with
that commit.

`docs/implementation-status.md` now binds its aggregate-green claim to clean run
`20260809T141527Z-8946c731`, exact commit `dc6b092`, and the retained result's
counts and provenance. Clean selected result
`20260809T161934Z-5d0bee6a` now binds Bridge 45/45, Documentation 1/1, and First-
Party Conformance 6/6 to exact commit `fdcf253`, with clean release-eligible
provenance and zero stderr/truncation. It is three explicitly selected steps,
not a current complete all-manifest gate. The available default
Community package artifacts are also older than the source
manifests, so they cannot substantiate current packaged behavior.

**Why it matters.** A senior team needs reproducible evidence that does not
depend on one long local agent session. A gate advertised as aggregate-bounded
now shares one tested remaining deadline, contains its provenance root, and has
one clean full local result. Hosted execution is still required to prove that a
fresh contributor/CI environment follows the checked-in workflow rather than a
long-lived developer machine's state.

**Underlying problem.** Verification breadth grew faster than verification
orchestration and evidence publication. The runner now owns command execution,
deadlines, package caps, and root containment. The remaining gap is immutable
hosted execution and publication for the same reviewed commit.

**Recommended direction.** Keep the new manifest/module split, package caps,
and clean local bundle. Execute the checked-in Windows managed/native workflow
for the same commit, retain its immutable artifact/link, and bind status claims
to that result. Keep hardware,
live-auth, real-controller, and physical-display
checks as explicit manual release gates rather than pretending hosted CI can
cover them.

**Tradeoff.** Migrating every custom executable test to a third-party framework
is not required immediately. The thin runner can provide bounded subprocesses
and interoperable results first. Streaming/capping output may truncate diagnosis,
so retain explicit truncation metadata and the tail. Native and AppContainer
tests may require separate permissions or self-hosted evidence; isolate those
rather than dropping the entire gate. Hashing every historical package is useful
for a forensic local bundle but can be expensive; the current 2 GiB/30-second
limits make that policy explicit. A release gate may later digest only manifest-
selected/current artifacts if measurement shows the broader inventory is wasteful.

**Resolution evidence.** The equivalent clean local bundle now satisfies the
bounded duration, managed/native result, provenance, and retained-log portion.
A clean commit must still produce a repeatable Windows CI
run with managed and native
results, documentation-link validation, deterministic package checks, exact
source/toolchain provenance, and retained logs. A deliberately hung case and a
child-process leak—including a parent that exits immediately while its inheriting
child keeps stdout/stderr open and a failed first termination attempt—must be
bounded and reported against stable suite/case IDs without hanging the next
suite or job. A deterministic start-order assertion must prove no child/user
code can execute before Job assignment, and an injected non-closing pump must
exercise the post-teardown deadline. The high-output and case-limit fixtures
must stay within their budgets and expose truncation metadata, the clean native
bundle must retain its compiler/SDK and immutable action revisions, and hostile
provenance fixtures must retain the implemented entry/total/root refusals and
shared-deadline reduction/exhaustion checks. A later focused-only milestone
must not leave documentation claiming that its HEAD passed the full aggregate.

### EQ-010 — P1 — Superseded YT Music reconciliation could commit stale failure state

**Status: Implemented in current HEAD; focused Release verification is reported, packaged proof remains.**

**Implementation.** `RunTransportRefreshBurstAsync` now routes attempt-local
retry status and authorization failure through context-aware helpers. Each
helper checks `WidgetOperationContext.IsCurrent` while holding `_stateLock` and
performs its status, pending-state, and connection mutation within that same
critical section. `ScheduleTransportRefresh` serializes same-lane admission on
the same lock, preventing a replacement from becoming current between an old
attempt's final currency check and mutation. A current reconciliation 401 still
clears pending state and returns to pairing; it no longer cancels its own lane,
because the delegate exits immediately. The ordinary non-operation connect and
poll 401 path retains lane cancellation and credential invalidation behavior.

**Why it matters.** Rapid controller input and slow local companion responses
are normal operating conditions. A stale response must not overwrite newer
intent, clear unrelated optimistic state, or force the user back through
pairing. This is precisely the class of generation/cancellation bug the public
Latest abstraction is intended to eliminate, so leaving failure commits
outside its currency contract also teaches authors an unsafe reference pattern.

**Underlying problem.** Attempt currency previously guarded successful data
commits but was not part of the failure-state ownership model. The correction
makes currency part of every attempt-local commit boundary.

**Policy.** A transport-reconciliation response is attempt-local and is ignored
after supersession or Active-lifecycle exit. Current 401 responses still force
pairing. A future flow that replaces credentials while requests are in flight
should add a host-owned credential/session generation rather than broadening a
stale attempt's authority.

**Tradeoff.** Ignoring every stale authorization response can temporarily leave
an invalid credential until the current request confirms failure. Treating
every response as globally authoritative can disconnect a newly paired or
otherwise newer session. Generation-bound invalidation is more explicit but
requires a small capability/client contract rather than a widget-local boolean.

**Resolution evidence.** The implementation agent reports that Release
`YtMusicWidget.Tests` passes 48/48 in 9.9 seconds. Source inspection confirms
that deterministic fakes deliberately ignore cancellation, admit a newer
transport command, and then fail the older request with ordinary and
authorization exceptions. Assertions prove stale failure cannot publish retry
status, clear the replacement's pending projection, disconnect, or cancel the
replacement, and that the newer result commits. A separate lifecycle-exit test
delays 401 until Active cancellation is observed and proves it cannot
disconnect. Existing current connect and poll 401 tests still prove credential
invalidation and pairing behavior. Packaged rapid-input and real-companion
failure evidence remains a release-validation follow-up, not a blocker to this
code-level finding.

### EQ-005 — P2 — The bug ledger no longer communicates release priority

**Status: Open; the latest ledger edit demonstrates an intake gap.**

**Evidence.** `docs/known-issues.md` currently lists 55 active issues; all are
`Verifying`, and 22 are P0. Several P0 summaries describe implemented SDK
helpers or sample migrations awaiting packaged evidence rather than an active
security, data-loss, or product-blocking defect. The file is approximately
1,680 lines and combines original symptoms, implementation narratives,
acceptance plans, repeated focused counts, and manual evidence debt.

The current ledger diff updates GBA-055 from YT Music 0.2.5/45 tests to
0.2.6/46 tests, but the focused suite now contains 48 cases after the stale-
failure correction. More generally, the ledger still accepts repeated green-
count prose without separating implementation state from packaged/manual
evidence, so it cannot serve as a concise source of release truth.

**Why it matters.** When 40% of the active ledger is P0 and every item has the
same status, neither a developer nor the user can tell what should stop a
release, what should be tested next, or what is simply missing evidence. It
also encourages the implementation agent to add prose and another feature
instead of closing a bounded set of verified defects.

**Underlying problem.** Defect severity, implementation state, release-gate
evidence, and roadmap delivery are represented as one flat issue list.

**Recommended direction.** Define severity separately from evidence state.
Reserve P0 for an actively exploitable security/data-loss issue or a core path
that cannot ship; P1 for release-blocking correctness/architecture; P2/P3 for
important and minor work. Move manual packaged/controller/display/auth checks
to a release-evidence matrix keyed to a smaller issue, and move generic SDK
delivery to the roadmap. Keep the active table short; archive closed narratives
with commit/evidence links.

**Tradeoff.** Reclassification must not hide real unverified behavior. Preserve
the acceptance criteria and history, but stop using severity as a proxy for
"important work the agent recently performed."

**Resolution evidence.** Publish severity definitions, identify the true
release blockers, give each active item one owner and next evidence action, and
show that the dashboard can answer: "what blocks the next build?" without
reading 1,600 lines. Record EQ-010's correction and fresh evidence while
keeping the contract-audit closure of EQ-009 out of the defect count.

### Managed logical-type hotspot register — `d534410`

A Roslyn declaration-span audit on the accepted `main` baseline aggregates all
partial declarations by logical type. It intentionally does not equate a long
file containing many small records/services with one giant class.

| Logical production type | Approximate declaration lines | Disposition |
| --- | ---: | --- |
| `SettingsWidget` | 1,328 in one non-partial declaration after accepted DLV-111 | Accepted DLV-036/044 removed presentation/selection policy and every production partial declaration, leaving one lifecycle/state/service-effect/committed-state adapter. DLV-111 grows the root from 1,208 to 1,328 lines for controller actions over theme selection/removal, while grouped presentation and the singular atomic filesystem mutation policy remain separate. Accept as a re-reviewed cohesive exception: no task, lock, timer, watcher, filesystem mutation authority, or second committed-state owner entered the root. Reopen for another service/coordination domain or material unrelated growth. |
| `SpotifyWidget` | 1,275 in one non-partial declaration after accepted DLV-043 | Conditional cohesive exception: the root is the sole lifecycle, provider-call, three cursor-resource, three task, two-lock/one-semaphore, committed-state, and invalidation owner over value-only route/action and playback policies plus one snapshot-only presenter. Reopen if presentation/policy returns, another lifecycle/resource/coordination owner appears, or material unrelated growth occurs. |
| `AudioMixerWidget` | 1,643 | Conditional cohesive exception after DLV-029/DLV-042: one committed model, action/effect adapter, selection, status, and invalidation owner; material growth or another coordination domain reopens it. |
| `UI` SDK facade | 1,471 across seven partial declarations | Cohesive exception: stateless public component-builder facade grouped by component family; it owns no lifecycle, tasks, locks, resources, or mutable model. Reopen if a component family starts sharing hidden state or cannot be tested independently. |
| `WidgetBridgeServer` | 616 after accepted DLV-040; `BridgeClientRegistry` remains the singular configured/current worker-generation, catalog mutation, residency, restart, replacement/removal, cached snapshot, dashboard sequence, and terminal-retirement owner | Conditional cohesive exception after DLV-039/DLV-045/DLV-040: the server retains pipe session, framing, handshake, authenticated request routing, Stop, and one serialized complete-or-abort reply/event write adapter. Closed read-only diagnostics and exact-token recovery projections now own the independent status/retry policy. Reopen if diagnostics policy returns, another pipe/session/write or dispatcher policy enters, client/catalog/residency ownership is duplicated, or either retained owner grows materially outside its named responsibilities. |
| `GamesAppsWidget` | 1,246 | Conditional cohesive exception after DLV-027: one lifecycle, provider-effect, action-admission, and committed-state adapter over separate presentation, catalog, persistence, and reconciliation policies. Reopen for store/collection/domain growth. |
| `NetworkControlsWidget` | 1,542 | Re-reviewed cohesive exception after DLV-092: one lifecycle, host-command, committed-state, and invalidation adapter over separate provider, command, action, identity, and presentation policies. Connection details reused the existing Active operation owner and provider-observation task; it added no task field, lock, semaphore, timer, polling loop, or second committed-state authority. Reopen for another provider/coordination owner or further material growth. |
| `WindowsNetworkNativeAdapter` | 404 | Conditional cohesive exception after DLV-041: the singular three-handle/three-callback/generation/publication/disposal adapter over value-based connectivity, WLAN, radio, and injected native-call policies. Reopen if another native lifetime, gate, callback-registration owner, or independent operation policy returns. |
| `WindowsNetworkPlatformBackend` | 1,012 | Re-reviewed cohesive exception after DLV-092: the sole MTA lifecycle, native-adapter lifetime, committed provider state, bounded invalidation channels, and publication adapter over separate command, bounded-admission, deadline/operation, reconciliation, connection-details selection, and event-projection policies. The new one-entry revision channel follows the existing native-callback coalescing model and adds no thread, timer, polling loop, lock, command queue, or public identity. Reopen for another queue/timer/lifecycle owner or further material growth. |
| `WindowsSpotifyPlatformBackend` | 1,019 | Conditional cohesive exception after DLV-034: singular integration identity, OAuth/PKCE, vault/token session, 401 replacement, local-player, lifecycle, and event authority; endpoint, retry, and parser policies are separate. Reopen if another auth/session or endpoint concern returns. |
| `WidgetProcessClient` | 964 | Conditional cohesive exception after DLV-037: the sole host lifecycle, restart-budget, failure-publication, and public-request adapter over one per-generation transport/resource/publication session plus focused request-correlation and gesture-reservation owners. Reopen if another lifecycle/session/publication/resource owner or material unrelated policy returns. |

The aggregate `Widget` SDK partial base is approximately 813 declaration lines,
below the numeric trigger. It is nevertheless reviewed explicitly: the base
owns lifecycle/invalidation and exposes thin creation/admission methods for
separate `WidgetModel`, operation, resource, navigator, queue, command, and
ticker owners. That is a conditional cohesive facade exception, not permission
to move those owners' mutable state back into the base. `PlatformServices.cs`
and `BrokerPipeTransport.cs` are long files containing multiple bounded types;
their file length is not a giant-class finding.

### EQ-006 — P2 — Advanced widgets remain application-sized monoliths

**Status: Open with bounded delivery coverage. Media Sessions, accepted
DLV-009/030 YT Music, accepted DLV-027 Games & Apps, accepted DLV-028 Network
Controls, and accepted DLV-043 Spotify prove useful lifecycle, presentation,
and responsibility boundaries. DLV-043 replaces Spotify's 2,207-line logical
partial type with one 1,275-line orchestration owner over closed value policies
and a snapshot-only presenter. Accepted DLV-029 gives Audio Mixer real
presentation and command-transition seams; accepted DLV-042 moves its residual
provider-session concentration behind one tested Active owner and gives the
remaining application root a precise conditional cohesive exception. DLV-026
independently owns the shared reverse-scroll defect.**

**Evidence.** Current primary declaration spans are approximately 1,643 lines
for Audio Mixer after accepted DLV-029/DLV-042, 1,206 for Network Controls after
accepted DLV-028, 1,246 for Games & Apps after accepted DLV-027, and 677 for YT
Music after accepted DLV-030.
Network Controls now also
has a 546-line pure presenter and focused provider, command, action, state, and
element-identity boundaries. Spotify spans roughly 2,040 declaration lines
across four files, but they are all declarations of the same partial type.
Line count is only a locator for the deeper ownership issue.

The migrations show three materially different outcomes:

- Media Sessions is the positive control. Its roughly 768-line primary file
  keeps render-facing data in `WidgetModel<State>` and transport admission in
  `WidgetOptimisticCommand`. A textual coordination inventory finds no
  `lock` or `SemaphoreSlim` use and only the lifecycle progress loop/task.
- Accepted DLV-009 (`08d44db`, integrated by `304102a`) gives YT Music one
  immutable presentation record and SDK-owned Active lanes for auto-connect,
  progress, polling, and Latest transport reconciliation. Accepted DLV-030
  (`549da57`, integrated by `6b9144d`) then reduces the primary owner from
  1,365 to 677 physical lines and moves closed action routing, connection
  transitions/safe status, companion confirmation/rollback and progress, and
  complete snapshot-only presentation into directly tested value seams. The
  widget remains the sole lifecycle, provider-client, committed-state,
  invalidation, and disposal owner with the same two narrow semaphores, one
  state lock, SDK operation registry, and zero Task/CTS registry fields. YT
  Music passes 55/55, Widget SDK 84/84, generic worker 9/9, and documentation
  52 in retained run `20260810T125844Z-07e6c6d2`.
- Spotify has successfully moved two offset collections into
  `WidgetPagedResource<TItem>` and one page family into an Active Latest lane.
  Accepted DLV-043 (`6c619e9`, integrated through `d534410`) removes every
  production partial declaration. The non-partial 1,275-line root retains the
  singular authorization/poll/progress task, refresh semaphore, active
  generation, cursor resources, provider effects, committed state, and
  invalidation. A closed route/action classifier and playback/device command
  policy exchange immutable values and own no lock, task, provider, resource,
  or invalidation state; the presenter consumes only an immutable snapshot.
  Direct policy/presentation tests plus the retained product matrix pass Spotify
  48/48, SDK 85/85, generic AppContainer conformance 6/6, and docs 54. The root
  remains above the numeric review trigger and therefore has the conditional
  cohesive exception in the hotspot register; later material growth must not
  silently broaden that exception.
  Accepted DLV-029 (`9647718`, corrected by `091ec51`, integrated through
  `6fc8d73`) moves complete snapshot-only presentation into a 351-line pure
  owner and output/input/session absolute-target transitions into a 354-line
  closed policy boundary. The first candidate exposed mutable targets,
  revisions, worker flags, and confirmation flags to the root and relied on
  one scheduling yield; review returned it. The correction makes transition
  state private, exposes immutable admission/work/acknowledgement/projection/
  terminal results, directly tests distinct domain paths, and proves
  cancellation-ignoring completion through a transitive task drain. The root
  falls from 2,496 to 1,880 lines and remains the only state lock, lifecycle,
  host-service/task, committed-state, selection/action, status, and invalidation
  owner. Retained run `20260810T155016Z-d0427e67` passes Audio Mixer 35/35,
  Widget SDK 84/84, Platform Broker 51/51, Windows Audio 15/15, and docs 52.
  This is material ownership reduction, but the root still owns four provider
  pumps plus initial snapshot, optional retry/attempt, and Active-lifetime
  coordination; DLV-042 dispositions that residual rather than declaring a
  nearly 1,900-line application root closed.
- Accepted DLV-042 (`37119f7`, corrected by `0a3635a`, integrated through
  `f64c35a`) moves that complete residual provider lifecycle into one 484-line
  internal Active session: linked lifetime, four subscription-before-snapshot
  paths and pumps, optional retry signals/attempts, bounded failure
  classification, and terminal drain. The session publishes immutable
  observations and owns no committed model, command transition, focus/action,
  status, view, or invalidation. The root falls to roughly 1,687 lines and
  crosses the boundary through one exact current-session reference/token,
  immutable observations, Retry, and Stop. Review returned the first commit
  because Retry could race semaphore disposal and a widget-side Loading write
  could overwrite a faster Healthy result; `0a3635a` makes retry/Stop atomic
  and leaves all retry-state ordering on the observation path. Retained runs
  `20260810T162728Z-cb2f0ef9` and `20260810T164357Z-e423fe7e` pass the assigned
  five-suite group and the bounded correction group respectively, ending at
  Audio Mixer 42/42 and Widget SDK 84/84. The residual root now has a cohesive
  exception as the single state-lock, action-admission, six host-control-call,
  command-task, committed-model, selection, status, and invalidation owner.
  Reopen it if material growth introduces provider lifecycle, presentation,
  persistence, or another independently testable coordination domain.
- Accepted DLV-027 (`df1dc81`, corrected by `69e86ef`) reduces the 1,861-line
  `GamesAppsWidget` to 1,274 lines and gives pure presentation (392 lines),
  schema-v3 policy (500), bounded catalog navigation (105), and CAS storage
  (65) named focused seams. The widget remains the single lifecycle, action,
  provider, and committed-state owner. The correction removed an unused render-
  time revision counter rather than replacing it with another coordinator and
  adds deterministic repeated presentation serialization. Games & Apps passes
  56/56 and documentation 52; the original focused lifecycle/private-state/
  worker/conformance groups also passed. This is a real ownership reduction,
  but the 1,274-line orchestration owner still warrants review before it is
  promoted as the public advanced-widget template.
- Accepted DLV-028 (`4ec931b`) reduces the 2,020-line Network Controls owner to
  1,241 lines and moves complete view composition, provider normalization and
  reconciliation, command admission/feedback, action vocabulary, and stable
  element identity into named value-based boundaries. The widget remains the
  sole lifecycle, host-command, committed-state, and invalidation owner.
  Coordination changes from one lock, one semaphore, one generation, one
  field cancellation source, and two detached observer roots to one lock, one
  semaphore, one generation, no field cancellation source, and one SDK-owned
  Active latest-operation lane. Deterministic tests cover provider events
  outrunning scan/connect acknowledgements, refresh replacement and four-
  subscription drain, direct policy behavior, and byte-identical repeated
  presentation. Focused evidence passes Network Controls 22/22, Widget SDK
  84/84, the generic worker 6/6, and 52 documentation contracts.

**Why it matters.** These are the examples external developers will copy.
Framework helpers improve correctness, but a human still has to understand a
large cross-cutting class to add a page, state, or action safely. Large files
also hide whether remaining complexity is domain behavior or duplicated
framework plumbing.

**Underlying problem.** Coordination abstractions are being introduced, but
production migrations have mostly been local substitutions rather than a
defined adoption architecture. Authors can discover useful primitives, yet no
advanced reference shows how state, provider/event merge, lifecycle work,
commands, navigation, and pure view composition fit together. Some remaining
policies are legitimately domain-specific. DLV-029 now separates Audio Mixer's
absolute-value command coalescing/confirmation and rendering; DLV-042 now
separates the provider-session lifecycle while the application root retains one
committed-state/action/status/invalidation transaction boundary.

**Recommended direction.** Treat Media Sessions and DLV-009/030 YT Music as
the behavioral lifecycle/state and responsibility baselines. DLV-030 keeps the
provider client and committed mutation in one orchestration owner while making
connection, authored companion confirmation, action routing, and pure view
policy independently testable. Require another advanced migration to reproduce
that ownership reduction before promoting it as a mandatory public template.

Spotify's accepted DLV-007 coherent keyed state remains the correctness
baseline. Accepted DLV-043 now preserves one lifecycle/committed-state/resource
owner while replacing partial-file field access with real route/action,
playback-reconciliation, and snapshot-only presentation boundaries. Retain its
conditional root exception and reopen it for material unrelated growth rather
than scheduling more line-count-only refactoring. DLV-006/DLV-022 continue to
own list/focus composition. Do not force Audio Mixer or Network Controls through a
generic abstraction prematurely. DLV-029 keeps its scalar transition machine
private to the audio domain. Accepted DLV-042 keeps all provider subscription/
fetch/retry/drain ownership together behind one Active session rather than four
stream wrappers or another committed-state owner. Preserve that conditional
cohesive exception and extract a reusable coordinator only if a second consumer
later proves the same shape.

**Tradeoff.** File splitting alone is churn and can make navigation worse.
Require each extracted type to reduce shared mutable state or enable focused
tests. Avoid a universal MVVM/base-class framework.

**Resolution evidence.** A new developer should be able to locate and change
one route, one provider action, or one visual state without reading the entire
widget. Track render/domain/coordination lines, author-owned tasks, cancellation
sources, semaphores/locks, explicit invalidations, and cross-file mutable
dependencies before and after. Require focused tests for cancellation-ignoring
stale success and failure, deactivate/destroy drain, provider-event versus
command reconciliation, confirmation timeout/rollback, and focus preservation.
A second advanced migration must reproduce the ownership reduction before the
structure is promoted as the public template.

### EQ-029 — P2 — Managed platform policy remains concentrated in central classes

**Status: DLV-031, DLV-032, DLV-034, DLV-035, DLV-036, DLV-037, DLV-041,
DLV-042, and DLV-044 are accepted and integrated. Remaining concentration has
bounded delivery coverage in DLV-039 and DLV-040. Existing EQ-020 remains the
authoritative liveness finding; EQ-022 records the accepted scheduling
boundary.**

**Evidence.** Before accepted DLV-031, `PlatformCapabilityBroker` spanned about
2,265 lines inside a 2,377-line file and owned identity/declaration/consent/
lifecycle authority, request leases, dashboard gestures, subscriptions, the
central capability switch, app-library caching, domain request execution, and
validation/projection for audio, network/Bluetooth, app library, media/Spotify,
loopback, secrets, and private state. `WidgetBridgeServer` is one roughly 1,380-line class
that combines pipe-session/framing ownership with request scheduling, worker
residency/client lifecycle, catalog and diagnostics publication, authority-
recovery operations, host effects, and failure projection. `WidgetProcessClient`
is roughly 1,026 lines, but its process-session/pipe/companion ownership may be
cohesive; length alone does not justify splitting it. `PlatformServices.cs` is
1,342 lines because it groups many small contracts and bounded service facades,
not because it contains one comparable monolith.
`WindowsSpotifyPlatformBackend` is a different case: one roughly 1,724-line
class owns configuration, OAuth/PKCE callbacks, token refresh/vault state,
retry/rate-limit policy, playback/devices/queue/playlists, local-player transfer,
response parsing, validation, and broker mapping.

Accepted DLV-041 (`3e6d779` plus `4060f4b`, integrated by `4630866`)
reduces `WindowsNetworkNativeAdapter` from 1,297 to 404 lines while retaining
the sole three native handles, three callbacks, adapter generation, event-
publication, recovery, and disposal authority. Separate value-oriented
connectivity, WLAN profile/scan/connect, radio-transaction, and injected native-
call policies retain no handle, callback, gate, task, or disposal ownership.
Exact-commit review rejected the first candidate because snapshot recovery could
re-register IP/connectivity notifications after disposal's cancellation pass
and callback publication could race past disposal. The correction gives the
same lifetime gate active/disposing/terminal states, exact-once cleanup,
admitted-publication suppression/drain, concurrent-disposer completion, and
safe reentrant handler disposal. Manually controlled barriers prove each
ordering without sleeps; retained run `20260810T185231Z-21f21f42` passes Windows
Network 51/51, Platform Broker 51/51, and documentation 52. The residual adapter
receives the conditional cohesive exception in the hotspot register; reopen it
if another native lifetime, callback-registration owner, coordination gate, or
independent operation policy returns.

Accepted DLV-035 (`51a6ec4` plus `f9df9b9`, integrated by `d151173`)
reduces the provider root from roughly 1,186 to 836 lines while retaining the
only MTA thread, native-adapter lifetime, committed snapshot, state lock,
channels, and publication authority. Closed command, operation/deadline,
reconciliation, event-projection, and 275-line bounded queue-admission owners
share values rather than provider state. The fixed queue admits at most 124
ordinary commands plus four deadline commands, while one highest-generation
overflow value per operation prevents arbitrarily delayed disposed-timer
callbacks from displacing the current deadline. Promotion preserves its
original cross-type arrival sequence and occurs only at a newly available FIFO
tail. A packed close/admission state balances a producer even after the bounded
close wait. Retained focused evidence passes Windows Network Provider 42/42,
PlatformBroker 51/51 on the original split, and documentation 52; no public API,
protocol, native adapter, owner thread, second state owner, or unbounded queue
was added. The residual provider root receives the conditional cohesive
exception in the hotspot register; accepted DLV-041 closes the separate native
handle/callback hotspot without moving provider ownership.

Accepted DLV-031 (`ffa1edc`, integrated by `27adec1`) reduces the broker
authority type from 2,377 physical lines/117,433 bytes to 837 lines/36,377 bytes.
Seven internal typed domain routes now own value-based decoding, validation,
backend execution, and projection for audio, network/Bluetooth/activity, app
library, media/Spotify, loopback, private secrets, and private state. The broker
retains the only identity/declaration/consent/lifecycle/request-lease/dashboard-
gesture/revocation/subscription/event-sequence authority. No public API,
protocol, task registry, cancellation source, lifecycle owner, lease owner, or
sequence counter was added. Retained run `20260810T132445Z-4465b4ae` passes
PlatformBroker 51/51, Windows app library 31/31, generic worker 9/9, and
documentation 52.

DLV-034 commit `a5c80ac` materially separates playback/collection endpoint
policy, bounded HTTP retry/rate-limit behavior, and strict response parsing
from the singular package-identity/OAuth/vault/token/local-player owner. The
central backend falls from 1,724 lines/81,619 bytes to 1,032 lines/48,862 bytes,
and retained focused run `20260810T140244Z-2b2f8821` passes the Spotify provider
32/32 plus PlatformBroker 51/51. Correction `344ab48` closes the returned
lifecycle-evidence gap with manually completed backend cases: a
cancellation-ignoring refresh cannot publish/cache its access token, rotate the
vault, reach the API, or supply the next request; Disconnect stops active local
playback, deletes the vault entry, clears cached authorization, and prevents
session reuse. Retained provider run `20260810T141733Z-0e24d4e1` passes 34/34
and documentation run `20260810T141833Z-a8567e3e` passes all 52 Markdown
contracts. The split and its corrected DLV-032 parent are integrated on `main`
through `a92378a`.

Accepted DLV-032 commits `8c11a27` and `9abdc6b`, integrated through `a92378a`,
move request admission, duplicate IDs, typed per-widget FIFO tails, fatal
cancellation, cleanup, and bounded drain into one 327-line dispatcher. A
118-line closed classifier replaces raw `JsonElement` scheduling convention.
The production deadline is two seconds; cancellation-ignoring work releases
IDs, tails, and slots at the deadline but remains observed in quarantine until
termination, with no late reply or fatal publication. The final retained run
passes Bridge 52/52; the grouped run retains WorkerHost 9/9 and the corrected
51/52 predecessor-failure regression. The server falls to 1,312 lines but still
owns client/catalog/residency and diagnostics/recovery in addition to its
cohesive session/framing/write role, so DLV-039 and DLV-040 disposition that
residual hotspot rather than calling line reduction closure.

**Why it matters.** Adding one capability or changing one scheduling rule
currently requires understanding distant policy and cleanup regions inside a
central trust or transport owner. That increases review cost at exactly the
boundaries where stale authority, cleanup, and malformed input must stay
consistent.

**Recommended direction.** Keep broker authorization/lease/event authority
singular while extracting typed capability-domain decoding, validation, and
projection policies under DLV-031. Extract only the bridge concurrency kernel
under DLV-032, leaving framing and session ownership explicit. Do not introduce
reflection dispatch, a service locator, a generic mediator, or one base class
per capability. DLV-034 separates Spotify provider session/transport/domain
policy only after DLV-023 identifies the failure owner. Reassess
`WidgetProcessClient` after those boundaries and the native asynchronous-client
work land; do not schedule a cosmetic split from its line count. Preserve
accepted DLV-035's provider policy/admission boundaries and accepted DLV-041's
separate native-resource boundary so neither regains a second owner thread,
WLAN handle, callback registration, committed provider state, or disposal
authority.

**Resolution evidence.** A developer can add or change one capability through
one named domain policy plus the explicit authorized route, and can change one
bridge admission/order rule through deterministic no-sleep dispatcher tests.
Authorization, request leases, event sequencing, framing, Stop, and outbound
writes retain one documented owner, with zero leaked tasks/IDs/slots/tails after
every tested failure and cancellation path.

### EQ-030 — P2 — Managed test suites are application-sized harnesses

**Status: Open with a bounded pilot in DLV-038 after the production-hotspot
queue. The executable-harness choice and absence of `Microsoft.NET.Test.Sdk` are
not themselves defects.**

**Evidence.** The largest managed test programs are currently about 3,360 lines
for Widget SDK, 3,338 for Widget Runtime, 3,234 for Platform Broker, 2,377 for
Games & Apps, 2,339 for Widget Bridge, 2,118 for YT Music, and 2,065 for the CLI.
They provide valuable deterministic coverage, but top-level registries, scenario
logic, process/clock/cancellation fixtures, setup, and assertions remain in the
same translation units. New focused production tests therefore keep increasing
the amount of unrelated test code a maintainer must scan.

**Why it matters.** Test code is production engineering infrastructure. A
developer changing one scheduling, capability, or lifecycle rule should be able
to locate the relevant scenarios and fixtures without understanding several
thousand lines, and reviewers should be able to distinguish changed behavior
from harness plumbing.

**Recommended direction.** Preserve the bounded executable runner, stable
ordered test names, exit semantics, verifier/JUnit extraction, and explicit
scenario registration. Pilot cohesive scenario and fixture owners in the three
largest active suites only after their production decomposition milestones land.
Extract a shared helper only when at least two suites prove identical semantics;
do not adopt a framework or reflection discovery merely to make files shorter.

**Resolution evidence.** Each pilot has a thin explicit runner; one scenario
family and its setup can change through a named owner; duplicated helper state
is reduced; names/order, deterministic timing, process containment, failures,
and focused case counts remain exact. Remaining large suites receive an explicit
cohesive exception or later bounded assignment.

### EQ-007 — P2 — Copyable documentation examples are not API-checked

**Status: Partially improved; known prose/API drift is corrected, but executable proof remains open.**

**Evidence.** The quickstart now uses the valid `WidgetGlyph.Refresh`, SDK
Gallery distinguishes protocol-v13 focus persistence from action routing, and
the authoring guide now consistently describes additive snapshot versions 1
through 13. `tests/Documentation.Tests/Program.cs` still checks relative links
and selected headings/phrases without compiling fenced C# examples or
validating referenced public symbols. The repository currently contains 70 C#
fences and 138 C#/PowerShell/JSON executable-looking fences across README and
`docs`; not every fragment should compile independently, but none is designated
as a canonical executable consumer by the documentation gate. The
NavigationShell and starter API examples are presented as copyable entry
points, so link-and-phrase validation remains insufficient.

**Why it matters.** Documentation is the primary SDK interface for a new widget
author. A first example that fails to compile makes the framework look
unfinished, sends developers into source inspection, and encourages invented
workarounds. It also allows API changes to silently invalidate many guides even
while the documentation suite remains green.

**Underlying problem.** Documentation contracts are checked as prose structure,
not as executable consumers of the public SDK.

**Recommended direction.** Keep important snippets in small compile-only sample
projects or extract marked fenced blocks into generated temporary projects that
reference the same public assemblies/package as an external widget. At minimum,
add symbol-contract checks for closed enums and command examples. Prefer one
canonical snippet source included by guides and tests over duplicated examples.

**Tradeoff.** Compiling every partial fragment requires cumbersome context and
can overconstrain explanatory prose. Designate only copyable/end-to-end blocks
as executable and leave illustrative fragments explicitly marked. A symbol
regex alone is cheaper but cannot catch overload, namespace, or lifecycle API
drift.

**Resolution evidence.** Compile the NavigationShell and canonical starter
examples against the public SDK in the documentation gate, and add a negative
regression proving an unknown enum member fails that gate. The corrected glyph,
protocol matrix, and focus-persistence wording are meaningful improvements, but
prose inspection alone is not closure evidence.

### EQ-012 — P2 — Data-only rendering must enforce its byte ceiling on consumed bytes

**Status: Resolved in current HEAD; focused Release verification is implementation-reported.**

**Implementation.** The first `RenderCommand` revision checked path metadata
and then reopened the file, which did not bind the limit to the bytes consumed.
The correction opens one read-only `FileStream` with `FileShare.Read` and passes
that exact handle to a bounded reader. A seekable preflight can reject an
obviously invalid file early, but the authoritative loop independently reads at
most 4 MiB plus one detection byte. A stream that reports one byte and produces
more than the limit therefore fails before deserialization. Only the captured
bounded buffer reaches `SnapshotJson.Deserialize`; there is no second path
open. Legacy `--type` and `--instance` options now return a usage error for JSON
input while DLL input retains its fixed isolation diagnostic.

**Why it matters.** The resource contract now belongs to one opened input
object rather than mutable pathname metadata. Concurrent replacement cannot
swap the consumed file on Windows, and misleading or non-seekable length
metadata cannot bypass the actual-byte ceiling.

**Tradeoff.** An actively written snapshot may fail to open until its producer
publishes it. Deterministic failure is preferable to inspecting moving bytes.
The bounded reader remains local to `RenderCommand`; no speculative shared
input abstraction was introduced.

**Resolution evidence.** Release `GbarCli.Tests` passes 48/48. The render case
covers a static limit-plus-one file, a custom stream that reports one byte but
produces 4 MiB plus one, rejection of obsolete JSON options, and unchanged
pre-existing output on DLL, option, and byte-bound failures. The ordinary valid
snapshot path remains green. The deterministic stream seam exercises the
resource invariant without scheduler-sensitive file-replacement sleeps.

### EQ-014 — P1 — Installed-package launch authority is not yet proven end to end

**Status: Materially partially implemented in commits `d2e49a9` and `6fc9e01`. Full package
inventory, per-start revalidation, pinned handles, and exact non-inheriting
AppContainer grants now cross the launch seam; end-to-end worker consumption,
path-to-object binding under directory replacement, alternate-group ACL policy,
one aggregate start deadline, and packaged abuse evidence remain open.**

**Current implementation.** `InstalledPackageIntegrity` now
computes path, length, and SHA-256 evidence for every package file from the same
bounded read used for the content-tree digest. `InstalledPackageLaunchLease`
requires the exact relative-path inventory, rejects reparse points and late
insertion, rehashes the whole tree, opens every verified file with
`FileShare.Read`, and keeps the file and required-directory handles alive.
`BridgeCatalog` carries a host-only content-lease factory into
`WidgetProcessClient`; every start/restart reacquires it before AppContainer or
process creation. `WindowsAppContainer.ReplaceReadAndExecuteGrant` removes the
old inheriting grant on the current authority root and gives the digest-derived
SID direct non-inheriting traversal/read grants for only the verified directory
and file lists. Runtime teardown releases the content lease alongside the
process residency lease. The bridge includes the content digest in each
installed generation's AppContainer identity so a later generation cannot
inherit a prior root's direct SID grants. `6fc9e01` additionally rejects a
package before extraction or launch when verified files would require more than
1,024 exact authority directories.

Commit `d2e49a9` also closes two narrower composition hazards. Runtime now
rejects a content authority root that contains or is contained by the trusted
generic-worker executable directory, preventing the exact-root purge from
weakening that separately trusted grant. Caller cancellation during content
acquisition also remains `OperationCanceledException` rather than being
misreported as the host's five-second admission timeout. Focused runtime source
adds direct cases for both behaviors.

This is the correct ownership direction and is substantially more than adding a
hash helper. Commits `d2e49a9` and `6fc9e01` include it; clean selected Release
Catalog 32/32, Runtime 44/44, Bridge 42/42, Worker Host 9/9, and First-Party
Conformance 5/5 suites are retained green in eligible run
`20260809T152831Z-67b77c73`. New
catalog tests inspect the full inventory, prove write/delete denial while the
lease lives, and reject mutation/insertion before admission. Bridge tests prove
factory wiring and pre-launch refusal. Runtime tests prove content admission
releases residency before launch, lease reacquisition/release across crash,
restart, and stop, and run a real AppContainer worker after replacing the same
SID's prior broad current-root grant; the verified text file is readable, the
late text file is denied, and package write is denied. A second real-token case
proves the next content-generation identity cannot read a root granted to its
predecessor. First-party conformance
now routes five real installed packages through the generic lease and exercises
YT Music suspend, restart, force reload, update, and removal. The committed suite
still does not adversarially exercise late/replaced managed dependencies, native
libraries, ordinary assets, failed ACL application, or
alternate AppContainer-group ACEs.

**Evidence.** `WidgetCatalog.DiscoverInstalledVersions` runs before
`SetEnabledAsync` mutates enabled state, and `BridgeCatalog.LoadWithInstalledAsync`
runs it again before publishing runtime authority. That correctly ensures the
digest shown in Settings is not merely trusted catalog metadata.

Current HEAD addresses the bounded-consumption subproblem with one narrow
catalog-internal `BoundedFileReader`. Manifest and 4 KiB integrity metadata now
open one `FileStream` with `FileShare.Read`, read at most the configured ceiling
plus one, and compare final consumption/length with the initial seekable length
before deserialization. `AppendExact` hashes exactly the encoded length, rejects
early EOF and one extra byte, and rechecks seekable length. Callers map those
failures to stable `invalid_manifest`, `invalid_integrity_metadata`, or
`package_tampered` results. This is the right ownership and avoids a generic
public I/O abstraction.

Three focused catalog cases now use an internal test seam appropriately. They
prove exact-maximum success, reject seekable streams whose reported length is
shorter or longer than content, exercise safe `int.MaxValue` sentinel
arithmetic without allocating the maximum, make `AppendExact` reject extra and
early-EOF bytes, and confirm static oversized manifest/metadata files retain
`invalid_manifest` / `invalid_integrity_metadata`. The same Release run rejects
a length that changes after consumption and a non-seekable limit-plus-one
stream. The implementation agent reports all 28/28 catalog tests pass; this
review code-inspected the cases but did not run them or inspect retained output.
These deterministic cases do not cover executable/dependency replacement or
execution.

The content-tree hasher was already stronger than the previous review stated:
every content stream uses `FileShare.Read`, which denies new write/delete
handles on Windows while that stream is open. The new exact-consumption check
makes that invariant portable and auditable. The unsupported
share-compatible-growth scenario remains removed from this finding.

The manifest semantic gap is now closed. `InstalledPackageIntegrity.Verify`
captures `manifest.json` from the exact handle whose bytes enter the tree hash,
parses that owned copy, and returns the model with the verified digest.
Discovery does not reopen the manifest. A focused case verifies the returned
digest/model and then mutates the path, proving the already returned manifest
remains the verified one.

The higher-risk gap has moved from missing launch authority to proving the
Windows authority implementation is exact for the full session. The content
lease blocks replacement/deletion of every verified file, and later files do
not inherit the new direct ACLs. That should bind entrypoint, lazy dependency,
native library, and ordinary asset path opens to pinned bytes, provided the
AppContainer has no other allow path. The implementation does not yet prove
that proviso. ACLs are persistent filesystem metadata: teardown disposes
handles but does not revoke the direct SID grants. The bridge now binds the SID
to the verified content digest, so a later generation uses a different identity;
focused bridge and production-token runtime tests prove the key changes and the
new identity cannot read the prior root. Same-digest relaunch remains gated by
whole-tree revalidation. The code purges only rules for the exact SID; it does
not assert that inherited
`ALL APPLICATION PACKAGES`, capability-group, or other AppContainer-token ACEs
cannot grant newly inserted package content independently.

There is also an unproven name-to-object seam inside the lease itself.
`EnsureTreeContainsNoReparsePoints`, each `EnsureNoReparsePoints` call,
directory `CreateFile`, file `FileStream` open, the final pathname inventory,
and `ReplaceReadAndExecuteGrant` are separate pathname operations. Directory
handles are opened without `FILE_FLAG_OPEN_REPARSE_POINT`; the code records no
volume/file ID or final handle path and does not open descendants relative to a
previously authenticated directory handle. ACLs are then applied by pathname,
not to an object identity returned by the lease. The restrictive shares are
valuable, but current source/tests do not establish that every directory name
still denotes the object whose children were hashed when a same-user writer
renames a directory or swaps a junction between those phases. The existing race
fixture mutates a file and inserts one file immediately before the final
inventory check; it never replaces a directory at any check/open/ACL boundary.
Until that binding is proven, documentation should say the implementation pins
verified file objects and denies ordinary late insertion—not that the entire
namespace is conclusively pinned.

The `7d92dcd` rotation confirms this boundary is unchanged. The lease still
returns string lists plus opaque handles, while
`WindowsAppContainer.ReplaceReadAndExecuteGrant` reopens every root, directory,
and file through `DirectoryInfo`/`FileInfo` and mutates its ACL by pathname. It
purges rules only for the digest-specific SID, applies directories and then
files sequentially, and returns no object-identity or rollback receipt. If a
later ACL write fails, earlier writes persist even though process creation is
aborted. The five-second content timeout has already ended before this method,
AppContainer setup, pipe creation, process creation, and hello begin. Existing
positive-token, late-file, stale-generation, and 512-file timing cases do not
exercise object replacement, alternate token-group ACEs, partial grant failure,
or the full start deadline.

The launch handoff now has the right distinct responsibilities.
`ProcessLeaseFactory` accounts residency; `ContentLeaseFactory` owns exact
verified content. `WidgetProcessClient` acquires them in that order, applies
content authority before process creation, reacquires on every restart, and
releases both on admission failure, connection failure, exit, unload, stop, or
session disposal. Keep this separation. The remaining work is to prove the ACL
capability rather than collapsing content and memory accounting into a generic
lease or exposing inventory mechanics to widget authors.

The catalog watcher is useful detection, not launch authorization. It waits
175 ms before a complete reload, and the current tamper test starts the worker,
modifies a file, explicitly reloads, then proves retirement. It does not mutate
after verification but before process/assembly load, test a mutate-and-restore
inside the debounce window, or prove that lazily loaded dependencies are the
ones hashed for the active authority.

The host-side GBSS gap is now closed in current HEAD without retaining
dormant file handles. The catalog computes an exact GBSS-relative-path/SHA-256
map while hashing the sealed tree and carries it only on the installed-version
model used by the bridge. `GbssFileSourceProvider` opens one restrictively
shared handle, enforces the byte ceiling on consumed bytes, decodes strict
UTF-8, and compares the bytes in fixed time with the inventory before parsing.
Installed style discovery consults the verified inventory rather than current
`File.Exists`. Modified sources fail their digest; late-added imports are absent
from the inventory and fail as missing. Focused Styling coverage passes 23/23,
including a ceiling-plus-one file, a misleading-length stream, digest mismatch,
strict UTF-8 rejection, and a late-added import.

Commit `d2e49a9` supplies the intended namespace invariant through
OS authority rather than modifying `PackageLoadContext`: verified files receive
direct read/execute grants and verified directories receive direct traversal
grants with no inheritance. The new production-token runtime test deliberately
preseeds a broad inheriting grant for the same SID on the current root, applies
the exact lease, and proves a late text file is denied while the verified file
is readable. That is strong evidence for the ACL mechanism and covers ordinary
file APIs. Extend it to a late managed DLL, native DLL, and asset under the real
installed-package bridge path, then inspect alternative inherited/group ACEs.
The generation-identity case now proves a new SID cannot read the stale old
root granted to its predecessor. Dictionary equality or host-side write refusal alone is
not enough, but the current worker-token probe is meaningful partial proof.

Public documentation may now describe the implemented digest-bound
worker-session consumption, but must qualify its remaining aggregate-deadline
and alternate-group-ACE gaps and must not call user-owned filesystem storage
physically immutable. The
inventory, lease, ACL, and hash mechanics remain host internals, not steps a
widget author should reproduce.

The committed `platform-architecture.md` makes that distinction: changed bytes
and namespace entries present during admission fail before process creation;
later insertions receive no worker authority. It also records the digest-bound
generation identity and leaves the aggregate start deadline open. Persistent
direct ACEs are generation-scoped rather than physically session-revoked, so
alternate-token authority and adversarial installed-loader evidence remain open.

The product roadmap's Phase 4 and risk register now name verified namespace/
launch consumption as a separate mandatory pre-public gate alongside signing
and revocation. This preserves the distinction: signing authenticates a
package digest but does not prove that the worker later consumes only that
signed tree.

**Why it matters.** The platform explicitly promises that replacement bytes
cannot inherit consent, configuration secrets, or update authority. The committed
lease design can satisfy that promise, but persistent or alternate ACL authority
would silently reopen the same defect while the code appears sealed. This is not
a P0 self-tamper claim: the capability-free AppContainer cannot normally modify
its package and the desktop user is outside the widget sandbox. It remains a P1
public-distribution boundary until the exact worker token is proven unable to
read replaced managed/native/asset content or late-added content through an
alternate group authority, and until ACL-applied pathnames are proven to remain
the same objects authenticated by the lease.

**Underlying problem.** Ownership is now modeled correctly across catalog,
bridge, runtime, and process lifetime. The remaining uncertainty is adversarial
enforcement and cleanup: Windows ACL grants outlive the C# lease object, may
interact with inherited/group rules, and are applied to names rather than a
lease-authenticated object identity. The installed-worker suite does not attempt
alternate path authority or directory replacement after positive startup.

**Recommended direction.** Retain the narrow bounded-reader design and its
overflow-safe `long` sentinel arithmetic and focused resource-bound evidence.

Commit `d2e49a9` implements the recommended lazy revalidation, complete
inventory, write/delete-denying handle lifetime, namespace ACL, and digest-bound
content-generation identity. Preserve the separate
`IWidgetProcessContentLease` boundary and keep it internal. Before public
distribution, prove the catalog root excludes alternative AppContainer-token
grants and make partial ACL-application failure explicit. Prefer a
small host-owned ACL authority object that can apply and roll back atomically;
if rollback fails, retire that package authority and surface a typed diagnostic
rather than swallowing it during general session cleanup. Do not expose hashes,
ACL paths, or lease acquisition to the SDK or worker.

Bind namespace traversal to authenticated objects as well as names. Prefer an
install-time protected generation that cannot be renamed by an untrusted
same-user writer, or a small Windows-native authority layer that opens reparse
points themselves, records volume/file IDs and resolved paths, opens children
relative to pinned directory handles, and applies/verifies security on those
same objects. Rechecking lexical paths more often is not an object-identity
contract. Keep this native complexity behind `IWidgetProcessContentLease`.

**Tradeoff.** The two-stage model hashes an enabled package during descriptor
publication and again on lazy launch, but avoids retaining handles for dormant
versions. A resident lease blocks uninstall until teardown; direct ACL mutation
adds startup work and persistent cleanup state; a protected generation costs
disk I/O/storage; and a unique SID per process can accumulate profiles. Measure
the selected model at 512 files and repeated start/stop/restart. Security is not
served by silently retaining stale grants to save cleanup complexity.

**Resolution evidence.** The new code-inspected catalog/bridge/runtime tests
establish inventory construction, pinned existing bytes, insertion/mutation
admission failure, exact factory wiring, sanitized errors, crash/restart/stop
lease lifetime, denial of a late text file under the production AppContainer
token after replacing a prior broad current-root grant, denial of a stale root
under the next digest identity, and positive execution of five real installed
packages. Clean selected run `20260809T152831Z-67b77c73` retains Catalog 32/32,
Runtime 44/44, Bridge 42/42, Worker Host 9/9, and First-Party Conformance 5/5
for the documentation-only commit containing `6fc9e01`. Extend the Windows installed-worker fixture to
load a verified managed dependency, native DLL, and asset after startup, while
matched late-added forms fail. Pause after
publication and after ACL application; prove replacement/reversion cannot
execute. Add deterministic phase seams around directory validation/open, file
open, final inventory, and ACL application; race directory rename and junction
replacement through each one, proving the operation either rejects or grants no
out-of-root/unverified object. Assert final volume/file identities match those
the ACL authority consumes. Across failed ACL application, failed process creation, connection
failure, natural exit, crash, restart, idle unload, disable/remove, and shutdown,
assert handle lease counts return to zero and partial ACL changes are rolled
back or the generation is quarantined. Add an inherited
`ALL APPLICATION PACKAGES`/alternate-ACE fixture and prove the catalog
ACL policy fails closed or removes it. Retain stable-tamper/live-worker retirement
coverage, then report startup time, handles, ACL operations, and transient
inventory memory at the 512-file and maximum enabled-widget bounds. A green
host-side factory test is not closure.

### EQ-020 — P1 — Exact-content startup can block the native UI outside every request deadline

**Status: Open at current implementation HEAD `0ff403a`; the security transaction
is now crash-journaled but remains synchronous and outside the content/connect
deadlines. The file inventory and managed request dispatcher are bounded, and
clean retained focused cases prove cooperative managed head-of-line behavior.
Production-client adoption, security-authority application/recovery, shutdown
drain, native pipe I/O, and one user-visible start deadline remain open.**

**Evidence.** `WidgetProcessClient.EnsureConnectedAsync` creates
`ContentLeaseTimeout` only around `ContentLeaseFactory` and
`ValidateContentLease`. That linked cancellation source is disposed before
`WindowsAppContainer.OpenOrCreate` and
`ReplaceReadAndExecuteGrant`. The latter synchronously performs
`GetAccessControl`/`PurgeAccessRules`/`SetAccessControl` for every authority root,
verified directory, and verified file, with no cancellation token, remaining
deadline, rollback object, or aggregate operation timeout. The worker pipe's
connect timeout is created later, after ACL work, pipe creation, companion
creation, and process start. A stalled security-descriptor call is therefore
outside both advertised deadlines.

`dc30be9` necessarily adds durable work to that unowned interval. Journal
admission can synchronously retry one global lock for five seconds using
`Thread.Sleep`, read or flush a document bounded at 16 MiB, recover up to 2,049
DACLs, capture the next full descriptor set, and flush it before apply. The dirty
`0ff403a` replaces pathname ACL helpers with safer handle-bound `GetSecurityInfo`
and `SetSecurityInfo`, but those calls, identity reads, verification reads, lock
wait, disk flushes, and crash recovery still receive no remaining deadline or
cancellation token. Security correctness improved; the user-visible liveness
boundary did not. Dirty run `20260810T005210Z-c46e6881` proves the ordinary
fixtures complete on this machine, not that blocked filesystem/security calls
or the maximum journal shape obey an enforced startup budget.

The default catalog permits 512 package entries. Runtime accepts up to 1,024
directory and 1,024 file grants. The new
`MaximumExactContentGrantIsBounded` case creates 512 flat files under one
directory and considers the entire `GetSnapshotAsync` successful if it finishes
within ten seconds. It does not exercise a near-maximum distinct-directory/path
shape, isolate ACL time, exercise a slow/failing ACL operation, or make ten
seconds a production-enforced ceiling. Clean selected run
`20260809T152831Z-67b77c73` retains 360.426 ms for this 512-file exact-grant path
and 325.283 ms for the separate 512-file launch-lease path on one machine.
Committed
`widget-packaging.md` and `platform-architecture.md` nevertheless say admission
is bounded to five seconds. That is not the implemented boundary.

The exact-edge test's activation stopwatch begins immediately before the
`Visible` lifecycle request and stops after the expected snapshot is received
and validated. That is a useful user-observable interval, but it does not
separate lease hashing, ACL application, process creation, pipe connection,
worker initialization, and render. Clean retained selected run
`20260809T155221Z-ae6e5d8d` records 2,528.883 ms for that interval and 376.140 ms
for packing. The run is neither repeated nor tied to a declared hardware
profile. Keep the ten-second assertion as a catastrophic regression ceiling if
it remains stable, but use a separate repeatable measurement lane for phase
timings and p50/p95/max claims.

Commit `6fc9e01` resolves the deterministic directory-count mismatch. Package
inspection now refuses more than 1,024 package-root/implicit directories with
`too_many_launch_directories` before extraction; installed-tree verification and
launch defend the same bound, and bridge coverage asserts the catalog and
runtime constants remain equal. The focused negative package needs 1,025
directories while remaining under 512 files. The follow-up now carries an exact
1,024-directory accepted package through public pack, install, enable, content
admission, ACL application, and real first-party launch. Clean retained selected
run `20260809T155221Z-ae6e5d8d` records 376.140 ms packing and 2,528.883 ms
through first validated render; the paired over-limit
CLI case proves no pack output or installed bytes are published. This closes the
shape-edge behavior in committed tests and clean release-eligible selected
evidence. It does not close the missing aggregate deadline or representative
p50/p95/max evidence.

Commit `d4291be` changes `WidgetBridgeServer.RunAsync` so it no longer awaits
ordinary requests in the sole pipe-read loop. It dispatches through 16 non-
waiting slots, tracks active IDs/tasks, preserves the framed write gate, chains
same-widget requests in receive order, fails duplicate active IDs closed, and
returns `bridge_busy` after saturation. Clean retained selected run
`20260809T161934Z-5d0bee6a` passes the Bridge harness 45/45 for commit `fdcf253`.
`StalledAdmissionKeepsControlPlaneResponsive` proves an unrelated catalog
response, seventeenth-request saturation, Stop
acknowledgement, cooperative cancellation, and residency release; the
pipelined-order and duplicate-ID cases cover correlation-aware FIFO mutation and
fail-closed ID ownership. This is a useful partial fix, not a deadline: each task can still
block inside the synchronous ACL path, all 16 slots can be stranded, and final
cleanup performs an unbounded `Task.WhenAll` after cancellation. A native caller
timing out still cannot cancel the Windows ACL call or restore partially applied
persistent grants. The tests do not cover the real ACL call or a cancellation-
ignoring drain.

More importantly, `WidgetBridgeClient` cannot consume this concurrency safely.
It has no single asynchronous read owner or pending-request table; every method
writes and then performs its own blocking read loop, rejecting a different
non-event request ID. The shipping `OverlayApp` therefore cannot pipeline the
unrelated catalog or Stop request used by the managed test while its UI thread
is blocked on admission. Treat `d4291be` as server/protocol groundwork, not
product-level control-plane responsiveness.

The native caller does not in fact have a request timeout. `WidgetBridgeClient`
uses synchronous `WriteFile` and `ReadFile` loops in `WriteFrame`/`ReadFrame`;
there is no overlapped I/O, wait deadline, or cancellable request object.
`OverlayApp::RefreshWidgetSnapshot`, lifecycle synchronization, restart, action,
and controller paths call that client from window-message/presentation work.
When the managed bridge stops replying, the overlay UI thread can therefore
block inside `ReadFile`: it cannot repaint a failure, accept B/Guide/Retry, or
drive its own shutdown. Correlated request IDs are already on the wire, but the
client still treats the pipe as a synchronous call stack.

Accepted DLV-054 (`546f014`, integrated through `8c1bbdf`) removes the new
artwork-demand instance of this gap without pretending the broader problem is
closed. A native cache miss now receives only a quick dispatch acknowledgement;
provider I/O and rasterization continue on managed global-dispatcher work, and
a later bounded event is admitted only for the still-current worker generation,
artwork session, and revision-bearing handle. The native cache consumes that
completion asynchronously, bounds both cache and result queues to 32, and
evicts the old decoded/renderer entry when a stable node receives a changed
handle. Focused bridge, broker, SDK, Games, renderer, and remote-image tests are
green, including the blocked-provider control-plane case. This closes only
DLV-018's artwork-demand contention and stale-revision defect; synchronous
native bridge calls, startup/ACL work, and session coordination remain open in
EQ-020.

**Why it matters.** A package within documented limits can still freeze the
native overlay thread before a worker process exists. One such request consumes
one managed slot; sixteen can deny ordinary requests, and shutdown can wait
forever for cancellation-ignoring work. In the shipping host, Close/Guide/Retry
cannot respond and Settings/recovery cannot send a bypass request because the
sole caller is blocked. The user receives no bounded typed failure, and repeated
ACL writes can consume seconds on the foreground interaction path. A test
ceiling of ten seconds is not a professional overlay startup target. An
installable package that is structurally impossible to launch also turns an
internal authority bound into an undocumented author trap.

**Underlying problem.** Content verification, namespace-authority mutation,
process creation, and handshake do not share one host-owned start-admission
budget. The code treats a bounded item count as equivalent to bounded wall-clock
work, and the persistent ACL mutation has no transactional owner capable of
rolling back a partially applied grant set.

**Recommended direction.** Define one `WidgetStartAdmissionBudget` spanning
residency reservation, content revalidation, ACL/protected-generation authority,
pipe and companion setup, process creation, PID/token verification, and hello.
Every phase should consume a shared remaining deadline and return a typed phase
failure. If Windows security-descriptor operations cannot be safely preempted in
process, perform authority preparation in a killable bounded helper or move it
to an install-time protected-generation publication step with explicit rollback;
merely wrapping the synchronous loop in `Task.Run` is not a hard bound. Keep a
slow per-widget start from blocking unrelated bridge requests, while still
serializing starts for the same widget and preserving aggregate residency
admission.

Move correlated bridge I/O off the Win32 presentation thread. The proposed
`WidgetSessionCoordinator` is the natural owner of an asynchronous request table,
per-operation deadlines, cancellation on host shutdown/catalog retirement, and
typed completion batches posted back to `OverlayApp`. Use overlapped pipe I/O or
a dedicated bounded transport thread that can be canceled by closing the pipe;
do not replace one UI-thread block with an unjoinable background thread. The
window layer should always remain able to paint `Starting`, transition to a
persistent typed failure, and process Close/Guide while admission is pending.

Choose a user-visible normal and maximum-package startup budget from retained
cold/warm measurements on representative hardware. It should be consistent with
the existing three-second connect expectation; do not encode ten seconds simply
because the first test machine passed it. If exact per-file ACL mutation cannot
meet that budget at 512 files, lower the operational file limit or choose a
protected-generation design rather than weakening namespace isolation.

Preserve `6fc9e01`'s canonical package-shape refusal and catalog/runtime equality
check. Keep the documented 1,024-directory ceiling fixed unless worst-shape
measurements justify a deliberate format/runtime compatibility change.

**Tradeoff.** A killable helper adds IPC and cleanup complexity; install-time
ACL/protected-generation work moves cost to acquisition and needs crash-safe
publication; a concurrent bridge dispatcher adds per-widget ordering state; and
a lower entry limit constrains packages with many assets. Any is preferable to
an unenforced timeout claim and partially mutated persistent authority on the
serial control path.

**Resolution evidence.** Add an injected authority-applier seam that blocks or
fails on an exact operation. Prove one aggregate deadline returns a stable
admission-phase error, no process starts, content/residency leases release,
partial grants roll back or the generation is quarantined, and list/Settings/
stop requests remain responsive. Retain cold and warm 1-file, representative,
and 512-entry results with separate hash, ACL, process, and hello timings plus
p50/p95/max. Exercise access-denied, security-descriptor write failure, catalog
disable during admission, caller cancellation, and shutdown. Update the public
five-second claim only when the enforced full boundary—not one factory and one
ten-second stopwatch assertion—matches the evidence.

The retained bounded-dispatcher cases cover
an unrelated catalog request, Stop acknowledgement, seventeenth-request
`bridge_busy`, duplicate active-ID refusal, same-widget ordering, pipelined
correlation, and cooperative resource release. Remaining dispatcher evidence is
a separate drain deadline for cancellation-ignoring work plus zero
request/slot/task leakage on that forced-abandon path.

Retain the exact 1,024-directory public pack/install/enable/launch case, the
atomic 1,025-directory refusal, and the bridge assertion that catalog and
runtime limits match.

Add a native transport/session test whose fake bridge accepts a request and
never replies. The window/message pump must remain responsive, a deterministic
deadline must publish one typed failure, B/Guide/Close must work during the wait,
late responses must be discarded by request/session generation, and shutdown
must close/cancel the blocked pipe without leaking its I/O owner. Repeat with a
slow reply arriving just before and just after the deadline and with an unrelated
catalog event/request while one widget start is pending.

### EQ-021 — P1 — Unified action admission is implemented; real failure-route proof remains

**Status: Native host composition implemented by `ddb66c2`; Verifying, not
resolved. Admission, lifecycle, compatibility, sanitized failure transport,
bounded per-widget presentation, timer/catalog/visibility ownership, and
advanced-widget managed adoption now have explicit owners. A real
bridge-originated Spotify/YT Music failure has not traversed native controller
ingress and the painted surface, and exact clean evidence is absent.**

**Evidence.** `WidgetControllerQueue.cs` now owns a shared 16-item
`ActionQueueState`. Internal `AdmitAction` accepts both direct and resolved
controller actions into one serial active-lifetime FIFO, coalesces only a
contiguous tail of changes to the same slider, reports `Enqueued`, `Replaced`,
`RejectedCapacity`, or `RejectedInactive`, cancels/drains on lifecycle exit, and
publishes later `ActionFailed` events. `WidgetWorkerServer` acknowledges direct
protocol `Action` after admission rather than after `OnActionAsync` completes;
`WidgetProcessClient.AdmitActionAsync` exposes that distinction while
`SendActionAsync` remains a compatibility admission wrapper.

`WidgetBridgeServer` routes bridge `Action` and the separate catalog
`QuickAction` command through `AdmitResidentActionAsync`, returns explicit
`action_inactive`/`action_saturated` failures, and emits asynchronous failure
events while preserving protocol-v1 reason `controllerActionFailed`. Runtime
source covers mixed direct/controller order, bounded admission, late failure,
and prompt acknowledgement of a hung action. The capacity case waits for an
explicit execution barrier, proves slider-tail replacement, and fills the exact
remaining capacity without a timing sleep. Public `controller-input.md` and
`declarative-ui.md` describe admission versus completion, queue capacity,
cancellation/drain, late failure, and legacy catalog QuickAction as explicitly
non-authorizing.

Commits `7d33ce1` and `7d92dcd` materially improve the native and diagnostic
boundary. `WidgetBridgeServer` includes the exact runtime generation in action-
failure events. `WidgetBridgeClient` requires a closed payload and
`canRestart = false`, rejects control-bearing or over-512-character messages,
and stores only validated widget/generation/action/source identifiers in a
bounded 16-item FIFO. `OverlayApp` discards mismatched runtime generations and
binds generic “action failed; try again” copy to the affected widget in both
dashboard and open-widget footers. `WidgetWorkerServer` publishes only the
stable generic `Action failed.` message across process boundaries. Native source
tests validate the client parser and oldest-entry eviction; runtime source
asserts the stable message.

Commits `7733a73` and `707f850` replace the lossy completion state with a
256-widget/runtime-generation store plus a deterministic visibility/deadline
controller. Transparent lookup keeps `MessageFor` and `Forget` allocation-free
inside `noexcept`; a dedicated one-shot deadline removes painted copy, and the
existing visible controller timer is an idempotent fallback.

Commit `ddb66c2` supplies the missing production composition boundary.
`WidgetActionFeedbackHost` owns only the bounded descriptor identity projection
and controller. `OverlayApp` injects monotonic time, Win32 scheduling, and
invalidation; passes every complete `TakeActionFailures` drain; reconciles
catalog generations; selects exact dashboard/open identities; and calls the same
seam from dedicated/controller timers, Hide, and Stop. The deterministic target
feeds two widgets through one adapter, proves offscreen isolation, exactly one
invalidation and timer schedule for a batch, one-shot expiry, fallback, stale
generation rejection, catalog replacement/removal, and no resurrection after
Hide or Stop. This is the thin host seam the prior review requested rather than
a second message framework.

Dirty full run `20260809T232451Z-15d2e7b6` retains
`WidgetActionFeedbackTests passed (305 checks)`, the complete native target/link,
41/41 manifest steps, and 784 JUnit cases. It is based on `ddb66c2` with
identical dirty start/end fingerprints and is correctly release-ineligible. The
test calls the adapter directly: it does not originate a real failed action in
an installed advanced widget, parse/drain it through `WidgetBridgeClient`, run
the actual WndProc timer, or inspect painted/UIA status. The older global
transient status slot also remains for synchronous input, reload, and snapshot
messages and still has render-time-only expiry; that is separate shell-status
ownership debt.

Both advanced widgets adopt the intended managed contract. YT Music removes its
broad action semaphore from ordinary refresh/transport commands and retains a
narrow `_connectionGate` for activation/connect overlap. Spotify awaits
ordinary commands directly and removes its action gate, command task registry,
start helper, and lifecycle drain. Their existing typed-fake tests remain useful,
and still call `OnActionAsync` directly. Commit `5804eaa` now additionally runs
Spotify as a real bundled and installed package in the generic AppContainer
worker, sends `spotify.next` through runtime admission, and proves the simulated
broker receives the exact Next command. It does not traverse the bridge/native
controller ingress or its asynchronous failure-presentation path.

The source harnesses register the associated Runtime, Bridge, SDK, Spotify, YT
Music, conformance, native-parser, and documentation cases, but this review did
not execute them. Clean full bundle `20260809T201448Z-0249ae81` passes 41/41
steps and 778 JUnit cases for exact clean commit `0598e5a`, covering the store,
timer/controller targets, packaged Spotify conformance, and broad baseline.
The real installed-widget failure from bridge parsing through the production
adapter into painted/UIA status remains absent rather than merely unretained.

**Why it matters.** The original transport-dependent worker-termination hazard
is structurally removed, and raw exceptions or stale generations no longer
reach the user. But accepted work is asynchronous: if its only completion
failure can be overwritten by another widget or remain visibly stale past its
declared lifetime, the product still cannot promise truthful action feedback.

**Underlying problem.** Ownership is now coherent from managed admission through
native presentation. The remaining risk is evidence composition: parser,
bridge queue, production adapter, timers, surface selection, and advanced-widget
failure behavior are proven in adjacent fixtures rather than one observable
production route.

**Recommended direction.** Keep the queue, allocation-free store, controller,
new host adapter, and compatibility rules stable. Add one production-path
fixture instead of another state abstraction: make an installed Spotify or YT
Music action fail after admission, carry its fixed event through the real
worker/bridge client into the host adapter, advance injected time, and inspect
the selected dashboard/open status (plus UIA live-region semantics where
applicable). Do not broaden the store into a generic message framework until
the older global status path is migrated deliberately.
Explicit widget-lifetime work such as browser authorization remains separate;
future ingress types must reuse typed admission and declare gesture authority.

**Tradeoff.** Unifying ingress changes when direct callers observe completion
and requires a bounded failure channel rather than synchronous exceptions. A
per-widget status map adds small state, but the descriptor catalog already gives
it a strict bound and cleanup key. Queueing pagination may require explicit
Latest/coalesced metadata rather than a separate widget task registry.

**Resolution evidence.** Retain a clean bundle proving the registered Runtime
and Bridge cases for prompt slow/hung admission, mixed-ingress FIFO order,
deterministic saturation, slider replacement, deactivation drain, late generic
failure, no restart, exact queued gesture authority, protocol-v1 compatibility,
typed Action/QuickAction responses, generation ownership, and restart/drain.
Preserve the new deterministic native host seam, including its two-widget
surface isolation, single invalidation/schedule, expiry/fallback,
generation/catalog, Hide, and Stop cases. Run both advanced widgets through the
real worker/bridge/native failure route and assert fixed copy, exact identity,
paint/UIA presentation, expiry, stale-generation refusal, and no worker restart.
Retain a clean exact-commit bundle containing the native store/parser/host
results, OverlayHost build, SDK, Spotify, YT Music, First-Party Conformance, and
documentation results. Until then, the architecture is implemented but the
product claim remains Verifying.

### EQ-025 — P2 — Spotify composes one screen from independently versioned state owners

**Status: Resolved by DLV-007 commit `ff706d2`, integrated on `main` as
`0941e41`. The reviewer inspected the exact diff and accepted retained focused
Release evidence: Spotify 35/35 and the minimal package seam 6/6.**

**Evidence.** At the pre-DLV-007 baseline,
`samples/SpotifyWidget/SpotifyWidget.cs` was 2,015 lines and one class owned
authorization, playback, destinations, queue/devices/local playback,
cache ages, selection, focus, page status, polling, progress, commands, every
action route, capability calls, and rendering. Its `Render` method copies about
a dozen scalar/reference fields under `_gate`, releases that lock, then reads
`_playlists.Snapshot` and `_playlistItems.Snapshot` from two independently
synchronized SDK resources. The resulting `WidgetView` therefore has no single
committed revision.

The selected-playlist path makes the consistency gap concrete. `OpenPlaylist`
commits `_selectedPlaylist`, mode, return focus, and initial focus under
`_gate`, then resets and starts `_playlistItems` after releasing the lock.
`Navigate`, `NavigateAndLoadAsync`, Back, and `ClearPageCaches` similarly update
manual state and reset the resource in separate critical sections.
`LoadSelectedPlaylistPageAsync` closes over the mutable `_selectedPlaylist`,
while `WidgetPagedResource<TItem>` accepts only `(offset, limit, token)` and its
snapshot carries page/status/error/focus/revision but no source key. A render
between the two commits can therefore pair playlist B's heading/route with the
previous playlist resource revision, or pair a route transition with a resource
snapshot from the prior route.

The Spotify suite has useful paging, cached-page, retry, Back/focus, sparse-page,
and cancellation-ignoring slow-detail tests. `SlowPlaylistDetailBack` proves a
late result cannot reopen the list after Back. None pauses immediately after a
new selection commits but before resource reset, renders concurrently, or
asserts that every playlist-detail snapshot's source key matches the selected
playlist. The fake detail provider returns one mutable `PlaylistDetail` and the
resource loader receives no explicit selection key, so the harness cannot state
that invariant directly.

**Why it matters.** This can transiently render tracks from one logical
selection under another heading and makes stale-snapshot action reasoning much
harder. Even if the resource epoch eventually rejects an old completion, the
screen assembled before that completion is not atomic. More broadly, Spotify
is supposed to be the advanced reference widget; copying this shape teaches
authors to combine several individually safe stores into an unsafe presentation
snapshot and to rely on narrow timing windows being harmless.

**Underlying problem.** State ownership was reduced locally but not composed.
The SDK resource owns its own epoch and cache, while Spotify owns the input that
defines what that resource means. No controller/model owns the invariant
“selected playlist key and playlist-item snapshot belong to the same revision.”
Splitting render methods into files would not fix that ownership boundary.

**Recommended direction.** First introduce an immutable internal
`SpotifyPresentationState` in `WidgetModel<TState>` (or an equally narrow
Spotify controller/model) for all render-facing scalar state. Bind playlist
detail to an explicit immutable `PlaylistSelectionKey` containing the playlist
ID and a selection generation. Every detail snapshot should carry that key;
render must ignore or show loading for a mismatched key. Commit selection,
resource reset, and load admission through one controller operation, then give
the renderer one immutable presentation snapshot. Start with a Spotify-local
keyed wrapper; promote a generic keyed resource API only after a second real
widget demonstrates identical semantics. Extract pure view files after the
state boundary is coherent.

**Tradeoff.** One immutable presentation state can become large, and copying
provider summaries has a cost; use immutable references and narrow domain
records rather than one universal reducer. A generic `WidgetKeyedResource`
could make the contract reusable, but publishing it from one sample risks an
unproven abstraction. A local keyed composition is the safer first step.

**Resolution evidence.** Add deterministic phase barriers rather than sleeps.
Pause after selection B commits but before the old detail resource is reset and
prove render cannot show A items under B. Pause an A load before completion,
select B, then prove A cannot publish into B even when cancellation is ignored.
Cover rapid Back/open, cached return, deactivation/reactivation, and both compact
and expanded focus restoration. Assert every rendered detail model carries the
same selection key as its heading and that an action from a stale snapshot is
rejected or maps only to the exact keyed item. Then execute and retain the
Spotify suite plus production worker/bridge conformance.

**Accepted resolution.** `SpotifyPresentationState` is now the single immutable
render-facing projection. Playlist detail carries a selection record keyed by
playlist ID and monotonic generation; selection/reset/load admission is atomic
under the widget owner, rendering no longer reads live resources, and only the
active route admits pagination. Deterministic provider gates cover late A
success/failure after B, refresh after newer selection, Back, and
deactivation/reactivation. Responsibility splitting and live Spotify proof are
separate follow-up work, not reasons to keep this coherence defect open.

### EQ-026 — P1 — Focus-edge pagination needs composed and live verification

**Status: Materially implemented by `5c3ce72` and strengthened by `023ea46`;
Verifying, not resolved. Real Spotify resources and exact native topology now
have deterministic 12/12/5 coverage, but no one fixture or live retest proves
the shipped controller/bridge round trip.**

**Evidence.** The reported run advanced through Spotify playlists until the
final provider page replaced the visible window with five rows; navigating Up
did not restore the cached preceding page. From the user's perspective, every
earlier playlist disappeared from the presented list, scrolling back up did not
load it again, and only the final five playlists remained accessible. Spotify
correctly uses 12-item
`WidgetPagedResource` windows, a six-page LRU, and previous/next actions on the
Scroll, so the data was not necessarily deleted—the host failed to admit or
present the reverse transition.

Commit `5c3ce72` changes `MoveWidgetFocus` to call
`DispatchScrollPagination` against the original focused node before explicit or
geometric movement. Only when no page action is admitted may focus leave the
Scroll. It also records the prior snapshot's `InitialFocusId` and lets a changed,
valid replacement value outrank exact/ordinal focus memory. Focused tests prove
the pre-move call order and a representative replacement from `page.14` to
`page.2`; public status documentation correctly leaves packaged reverse paging
open.

Commit `023ea46` materially closes the generic-fixture gap. The Spotify suite
now builds 29 real playlist and detail items for wide and compact views, walks
12/12/5 pages in both directions, joins repeated input to a slow cancellation-
ignoring load, checks absolute IDs/provider counts/cache hits, and rejects a
nonexistent final forward page. Native tests use the exact Spotify scroll/rail
IDs, prove Down/Up edge actions for 12-row and five-row trees, restore the
entering edge, and show that remembering it prevents an unrelated refresh from
stealing focus. Dirty run `20260809T223920Z-37fcdf1a` records Spotify 32/32,
Focus Navigation 47 checks, Widget Surface Focus 24 checks, and 41/41 aggregate
steps/784 cases green.

The remaining evidence gap is still composition. `PressPagedDirectionAsync`
implements its own visible-index move, reads the Scroll action, directly calls
`widget.OnActionAsync`, and then polls replacement snapshots. It never enters
native `MoveWidgetFocus`/`DispatchScrollPagination`, bridge admission, or
presented focus memory. Conversely, native tests construct independent node
trees and never consume a real Spotify snapshot or provider transition. Detail
tracks reverse only final -> middle, not middle -> first. The passing broad run
also overlapped another run in the same checkout and is dirty/ineligible.

**Why it matters.** This was a user-visible loss-of-navigation defect in the
most demanding sample and in a public SDK workflow. Helper-level confidence is
not enough: authors should not need visible paging controls or private host
focus knowledge to make `WidgetPagedResource` reliable.

**Underlying problem.** The ownership order is now substantially corrected,
but the cross-process contract is still inferred from independently tested
helpers. Page admission, replacement identity, and presented focus need one
observable invariant.

**Recommended direction.** Preserve pre-move pagination and the new exact
Spotify resource tests. Compose rather than add more parallel helper cases:
feed an actual compact/expanded Spotify snapshot into the production native
edge resolver, carry its admitted action through the bridge/worker, then apply
the returned snapshot through the production focus-memory transition. Keep the
old page/focus through slow or failed loads, coalesce repeated edge input, and
let ordinary movement resume only when no valid page action exists. The new
one-shot consumption check makes another protocol token unnecessary unless the
composed fixture finds a real ambiguity.

**Resolution evidence.** Turn the new three-part proof into one host-level
fixture with the real serialized Spotify compact and expanded focus graph and a
29-item 12/12/5 sequence. Drive directions rather than directly invoking the
Scroll action; assert admitted bridge action, absolute visible/focused IDs,
provider counts, reverse cache hits, and no rail/tab escape. Complete detail
reverse to page one and include slow/failing load, rapid input, unrelated
playback refresh, responsive reflow, and cache eviction. Then have the user
repeat the originally failing live controller flow and retain a current
packaged smoke result.

### EQ-023 — P1 — UIA shell authority and route equivalence are not yet proven end to end

**Status: Materially advanced through commits `9ec0374` and `6a079b6`; the
inspected origin and nested-Back semantic mismatches are implemented.
Cross-language ownership, real route proof, and clean release evidence keep
this a P1 ship gate.**

**Evidence.** Commits through `b3558f9` provide real widget/tray/dashboard UIA,
closed patterns/events, safe provider lifetime, coherent presented slider
values, quiet help/status, direct tray selection, and one composite open-widget
tree. Clean retained result `20260809T201448Z-0249ae81` passes 41/41 steps and
778 cases for exact clean commit `0598e5a`; it predates `b3558f9`, `59aae1a`,
`6162937`, `8a46d5f`, `9ec0374`, and `6a079b6`. Newer broad evidence is dirty
and does not close this accessibility finding.

Commit `59aae1a` adds a closed `PhysicalController` versus
`AccessibilityAutomation` origin to `ControllerInputEvent`. Native UIA dispatch
marks automation explicitly. The bridge validates the enum and refuses to mint
dashboard authority unless origin is physical. `WidgetProcessClient` rejects an
authority/origin mismatch, and the base SDK queue supplies no
`WidgetCapabilityGestureContext` for automation. Bridge, runtime, and SDK tests
exercise ordinary automation action admission, unknown-origin rejection, legacy
physical omission, and refusal of an explicitly paired automation authority.
This is a strong correction and public documentation now states the policy.

Commit `9ec0374` makes that defense independent at the two missing layers.
`WidgetWorkerServer.HandleRequestAsync` validates `ControllerInputOrigin` and
enters `WidgetCapabilityInvocationContext` only for a physical dashboard event.
`BrokerWidgetCapabilityClient.InvokeAsync` retains gesture metadata only after
the exact activation callback returns true and the context is still active;
denied activation falls back to an ordinary non-authorizing request. The new
custom-widget fixture invokes a capability synchronously from an automation-
origin override and observes no ambient context, activation, provider call, or
companion grant. The broker fixture pre-grants a valid authority, returns false
from activation, and asserts the provider is not called, so it would fail if
the denied gesture metadata were forwarded. This source shape resolves the
inspected mismatch. Dirty broad run `20260809T220617Z-bc1dd7a3` records Runtime
49/49 and all 41 steps/783 cases green, but its provenance is dirty base commit
`8a46d5f`, not exact clean `9ec0374`; this review did not execute it.

Commit `6162937` addresses both prior composite defects directionally.
`ElementDomain` now participates in AutomationId, runtime identity, lookup,
event diffing, and queued actions; duplicate same-domain identities fail closed,
and a provider test gives widget and host nodes the same raw ID. A typed
`BackWithinWidget` binds to the current scope, revalidates widget/runtime/
snapshot/scope, and sends automation-origin B rather than an arbitrary action
string. Focused helper/provider tests cover collision isolation and stale scope.

Commit `6a079b6` closes the specific known semantic mismatch. It
passes `focusedElementId_` into publication and invocation revalidation and
mirrors the managed resolver for focusless root disabled/busy state, focused
shortcut precedence, focused-owner disabled/busy suppression, ancestor
fallback, stale focus, and exclusion of nested input scopes. Native cases cover
those branches. It does not yet provide full route proof: no real
UIA/`OverlayApp` test invokes Picker, ActionSheet, or `WidgetNavigator` and
observes the worker route return, and the two languages still own separate
algorithms that can drift. The reviewed vector allocation is fixed: final
`ResolveFocusedBackInScope` carries a closed result through allocation-free
recursion under the protocol's bounded tree depth, so the `noexcept` path is
truthful. Dirty run `20260809T221800Z-2c278bf3` records Host Accessibility 32
checks, SDK coverage, and all 41 steps/783 cases green from a dirty worktree;
it is not clean exact-commit proof. The `widget:`, `host:`, and `tray:`
AutomationId change remains sensible for the preview but should be an explicit
compatibility contract for external automation clients.

Typed choice/toggle semantics, deliberate OS HWND reuse, legacy MSAA, packaged
AppContainer/UIA proof, and a Narrator pass also remain open.

**Why it matters.** Accessibility actions must not acquire capability authority
through a weaker origin or expose navigation that becomes inert at the worker.
Composite identity and Back behavior are public platform contracts: authors
should neither reserve hidden prefixes nor duplicate host navigation, and UIA
clients need stable IDs and one route equivalent to controller behavior.

**Underlying problem.** Origin is now explicit and `9ec0374` makes each
security layer locally fail closed. `6a079b6` mirrors nested Back semantics, but
availability is still independently reconstructed in C++ instead of
derived from the same semantic resolution contract used by the SDK. Current
tests prove components, not the real host-to-worker route.

**Recommended direction.** Preserve `9ec0374`'s physical-only worker
context, enum validation, activation-gated metadata, ordinary denied fallback,
and adversarial custom-widget test. Add explicit unknown-origin worker coverage
if the existing runtime case does not traverse this validation method, then
retain focused plus clean aggregate evidence. Keep the runtime
reservation and companion checks as separate defenses.

Preserve the domain-aware identity work and make AutomationId versioning/release
notes explicit. For nested Back, either share a closed resolver result across
the native/managed seam or
drive both implementations from one exhaustive conformance corpus, including
focusless, disabled, busy, missing, stale, changed-scope, and nested-scope
states. Keep the typed scope target and automation origin; never carry an author
action string as host authority.

**Tradeoff.** Waiting for successful activation before attaching metadata adds
one branch but makes the least-privilege claim locally true. Sharing a resolver
result may require a protocol field; mirroring it avoids a protocol revision but
creates permanent cross-language conformance work. Namespaced AutomationIds can
break preview automation scripts, while leaving raw collisions visible breaks
identity correctness; document the intentional change.

**Resolution evidence.** Retain the new adversarial origin cases on a clean
exact commit and close any worker unknown-enum gap. For resolver equivalence,
use a real `IUIAutomation` client
through `OverlayApp` to open and exit SDK Gallery Picker, ActionSheet, and a
Navigator route, including no-focus, disabled/busy, stale snapshot/scope, resize,
and worker-failure cases. Prove colliding raw IDs retain distinct traversal,
events, patterns, and exact action authority. Then retain a clean aggregate,
AppContainer self-automation denial, deliberate HWND reuse, and packaged
Narrator/MSAA evidence over Settings, YT Music, and Spotify.

### EQ-024 — P2 — Implementation commits can overwrite independent review disposition

**Status: Open after documentation commit `689a933`; all implementation commits
through current HEAD `0be052b` correctly left both
reviewer-owned documents untouched and reported their milestones in
`docs/implementation-status.md`, so this cycle shows no regression. The
repository workflow still does not prevent recurrence.**

**Evidence.** Commit `689a933` is titled `docs: close unified action admission
review` and stages `docs/engineering-quality-review.md` plus
`docs/widget-authoring-experience-review.md` from the implementation stream. It
changes EQ-021 to Resolved, says focused Release suites pass, and reduces the
remaining action task to retaining an aggregate bundle. At that commit, source
still showed multi-widget feedback collapse and unscheduled expiry invalidation,
and `artifacts/verification` had no result for `6c5f932`, `7d33ce1`, `7d92dcd`,
or `689a933`. Later commits through `ddb66c2` now address that product defect
without self-editing the reviewer disposition; runner commit `7c8a5b8`
preserves the same path ownership. The 445-line engineering-review
delta also includes independent
review work that was already dirty, so implementation and reviewer authorship
cannot be recovered cleanly from the commit boundary.

**Why it matters.** An implementation agent cannot independently adjudicate its
own finding and still provide the assurance model requested by the user. A
green self-edited review can hide missing evidence, and staging shared dirty
files can accidentally attribute, overwrite, or ship reviewer work with an
unrelated product milestone. That makes both the audit trail and future diff-
based reassessment unreliable.

**Underlying problem.** The repository has two logical authors operating in one
worktree but no path-ownership or staging rule. Review documents are being used
simultaneously as implementation input, implementation status output, and the
independent reviewer's disposition ledger.

**Recommended direction.** Reserve the two review documents to the independent
review stream. The implementation agent may read them, report a proposed
finding disposition in its milestone summary, and record exact commit/result
identifiers in `docs/implementation-status.md`; it must not edit, stage, or
commit either review file. Use path-specific staging and inspect the staged name
list before every implementation commit. Do not use `git add -A` in a shared
dirty worktree. The reviewer alone updates finding status after inspecting the
implementation and evidence, and continues not to commit so the user retains
explicit control of the review artifact.

**Tradeoff.** Review changes may remain dirty across several implementation
commits and require careful path-specific staging. That inconvenience is the
cost of genuine independent disposition. Creating a separate worktree could
give stronger isolation, but it would delay visibility of live implementation
changes and is unnecessary if both agents honor narrow file ownership.

**Resolution evidence.** Add the ownership rule to the implementation goal or
repository agent instructions. During a later cycle, leave a deliberate review-
document edit dirty, make an implementation milestone, and prove the commit's
staged paths exclude both review files while their working-tree bytes remain
unchanged. The implementation report should cite evidence without declaring the
review finding closed; the subsequent reviewer cycle should perform and record
that disposition independently.

### EQ-022 — P2 — Bridge scheduling policy is embedded in the transport session

**Status: Resolved for scheduling by DLV-032 commits `8c11a27` and `9abdc6b`,
integrated through `a92378a`. The residual multipurpose server is separately
open under EQ-029 and DLV-039/DLV-040.**

**Evidence.** `WidgetBridgeServer.RunAsync` now owns a 16-slot
`SemaphoreSlim`, `activeRequestIds`, `requestTasks`, a lock-protected
`widgetRequestTails` dictionary, a shared `fatalRequestException`, and
`ContinueWith` cleanup in addition to pipe acceptance, handshake, event
subscriptions, the read loop, Stop, and client disposal. `DispatchRequestAsync`
waits on the raw per-widget predecessor, while each `ClientRegistration` also
has an `OperationGate` that serializes the typed widget operation. Ordering is
therefore expressed twice at different layers.

`RequestWidgetId` reads a `widgetId` string from the unvalidated `JsonElement`
to choose the scheduling key, and `HandleRequestAsync` later deserializes the
same payload into its message-specific DTO. The naming rules currently match,
but adding a widget-scoped message now requires maintainers to remember the
implicit raw-property convention or silently lose receive-order chaining.

The new tests are pipe-level integration fixtures. Two require Windows because
they simulate admission through a `ConfiguredWidget`; the ordering case uses a
real worker and a 150 ms `Thread.Sleep`. They exercise valuable paths, but there
is no direct deterministic seam for different-widget parallelism, failure before
and after handler admission, cancellation while waiting on a predecessor,
capacity release on every outcome, fairness, or forced drain when work ignores
cancellation.

Candidate `8c11a27` removes the server-owned semaphore, active-ID/task
registries, ordering lock, widget tails, fatal slot, and scheduling continuation
into one 214-line internal `BridgeRequestDispatcher`. Its manually controlled
tests cover success, ordinary failure, cooperative cancellation, same-widget
FIFO, different-widget progress, predecessor failure, duplicates, capacity, and
cooperative forced drain without sleeps. The candidate is not closure: its
production `CancelAndDrainAsync` still awaits `Task.WhenAll` with no deadline,
the forced-drain handler honors the dispatcher token, and `RequestWidgetId`
still reads the raw unvalidated `JsonElement` convention before request-specific
strict decoding. The retained focused run `20260810T133940Z-18b2982e` passes
Bridge 51/51, worker 9/9, and documentation 52, but those green cases do not
exercise the missing cancellation-ignoring deadline or typed malformed/unknown
classification boundary.

Correction `9abdc6b` supplies the missing boundary. Production drain has a
two-second deadline; handlers that ignore cancellation relinquish request IDs,
FIFO tails, and capacity slots while remaining explicitly observed until they
terminate. Their late reply path uses the canceled request token and their late
fault cannot publish into the closed or replacement session. One closed typed
classifier strictly decodes known payloads and gives malformed/unknown requests
no widget key. Manually controlled tests cover deadline timing, two quarantined
handlers, disposal before their completion, zero admission/FIFO state, eventual
observation, no late reply/fatal, malformed payloads, invalid IDs, and unknown
types without sleeps. A grouped run retained a real predecessor-failure
regression at 51/52 while WorkerHost passed 9/9; after suppression was narrowed
to handlers that actually started under dispatcher-owned drain, final Bridge
run `20260810T145134Z-be1c9952` passes 52/52 and documentation run
`20260810T145223Z-665e4ef0` passes all 52 files. These are stable dirty-worktree
assignment artifacts rather than release evidence.

**Why it matters.** This is the bridge's concurrency kernel. A missed cleanup
can leak one of only 16 slots, a missed request classification can reorder state,
and an exception path can strand a task tail or turn a request error into a
session failure. Keeping those invariants interleaved with pipe/session code
makes review and extension disproportionately risky—especially when the native
client is later made asynchronous and begins exercising real concurrency.

**Underlying problem.** Admission, ordering, execution, completion, and drain
are a lifecycle-owned policy but are represented as local collections and
continuations inside the transport loop. `OperationGate` then supplies a second
implicit serialization policy after admission. The code has bounded data
structures, but no single type states or enforces the scheduler contract.

**Recommended direction.** Extract one narrow internal
`BridgeRequestDispatcher`, not a generic task framework. Give it a typed
`BridgeRequestKey` produced once during strict request decoding, containing the
request ID and optional widget ID. The dispatcher should own global and
per-widget admission bounds, duplicate-ID refusal, FIFO tails, completion
cleanup, fatal transport cancellation, and a separately bounded drain. It
should execute an injected request handler and return an explicit admission
result; `RunAsync` should remain responsible for reading validated envelopes,
the reserved Stop lane, and writing the resulting reply/event.

Decide whether `OperationGate` remains the authoritative widget-state mutex or
whether receive-order execution makes some uses redundant. Do not leave two
undocumented ordering layers. Preserve the write gate until there is exactly one
outbound frame owner.

**Tradeoff.** A separate dispatcher adds a type and an internal request-key
model. That cost is justified only because it removes mutable concurrency state
from the session loop and makes the invariants directly testable; a generic
queue abstraction or callback-heavy facade would be worse than the current
explicit code.

**Resolution evidence.** Add cross-platform, no-sleep tests with injected
manually completed handlers for global/per-widget capacity, duplicate IDs,
same-widget FIFO, different-widget parallelism, malformed/unknown request
classification, handler success/failure/cancellation, predecessor failure,
session cancellation, and a cancellation-ignoring drain deadline. After every
case assert zero active IDs, slots, tails, and tasks. Retain the named-pipe tests
as framing/integration proof, then add the production asynchronous native-client
test required by EQ-020.

### EQ-027 — P1 — Verification overlap is guarded, but release input remains mutable

**Status: Partially implemented by committed runner repair `7c8a5b8`. A live
lease now prevents concurrent `Verify.ps1` runs and schema-v2 endpoint
provenance prevents persistent worktree/commit changes from remaining eligible.
No clean exact-commit bundle exists, and the leased checkout is still mutable
by non-verifier writers during execution.**

**Evidence.** Full runs `20260809T223902Z-17e88fe6` and
`20260809T223920Z-37fcdf1a` started at 15:39:02 and 15:39:20 in the same
checkout. The first failed `network-controls-tests` with compiler error CS2012:
`NetworkControlsWidget.dll` in shared `obj/Release/net8.0` was locked by another
process. The overlapping run passed 41/41 steps and 784 cases. This is a runner
ownership failure, not a Network Controls regression.

Commit `7c8a5b8` changes `VerificationRunner.psm1` and `Verify.ps1` to acquire
`artifacts/verification/.repository-run.lock` with a live read/write handle and
read-only sharing before provenance, and release it only after final result
publication. Owner PID/run/configuration/start metadata is informational; file
bytes never confer authority. A bounded optional wait shares the overall
deadline. `Test-VerificationRunner.ps1` starts separate PowerShell processes and
proves refusal before the contender writes its marker, acquisition after normal
release, and acquisition after forced holder death.

Schema-v2 results capture start and finish commit/full porcelain fingerprints,
final package hashes, stability, and explicit ineligibility reasons. Pure helper
cases reject start-clean/end-dirty and changed-commit endpoints. Focused run
`20260809T231421Z-73303441` passes the runner self-test. Full runs
`20260809T231747Z-d9e41dcc` and `20260809T232451Z-15d2e7b6` pass 41/41 steps and
784 cases in 351.009 and 326.534 seconds. The latter records exact commit
`ddb66c2`, identical dirty fingerprints, 29 final package digests,
`repositoryStateStable: true`, and only `starting_worktree_dirty` plus
`finished_worktree_dirty`; `releaseEvidenceEligible` is correctly false. The
implementation-status claim that a second real wrapper invocation was refused
has no separate retained artifact this review could locate, although the
checked-in process fixture proves the same lock mechanism.
Newer full run `20260809T234957Z-6afea078` also completes without overlap: 41/41
steps and 787 cases in 367.615 seconds for commit `7c8a5b8` plus dirty authority
WIP, with identical dirty endpoints and correct release ineligibility. It still
does not supply a clean immutable runner-commit bundle.

Two boundaries remain. First, the lease is cooperative only among verifier
invocations. An editor, implementation agent, `dotnet build`, or native build
does not acquire it. Start/end equality detects persistent changes but cannot
prove that a tracked source file was not edited and restored while a step read
it, or that an ordinary build did not touch the same output tree. Second,
`Get-VerificationEvidenceEligibility` evaluates pass/clean/endpoint stability
without configuration, lane, or selected-step scope. The result does retain
those fields, so a careful consumer can distinguish a focused result, but the
single `releaseEvidenceEligible` name is not a full-gate assertion by itself.

**Why it matters.** A release gate must own the code and artifacts it tests.
The commit removes the reproduced verifier-versus-verifier file-lock failure and
substantially improves honesty for ordinary persistent edits. A clean endpoint
pair still does not make a mutable development checkout an immutable execution
input, and a scope-agnostic eligibility bit can be over-read as a complete
release gate.

**Underlying problem.** The runner now owns result logs and serializes its own
shared-output users, but it still verifies in the actively edited checkout.
Endpoint provenance describes two observations, not immutable source custody.
Evidence cleanliness and release-gate scope are also represented by one field.

**Recommended direction.** Retain the live lease, final provenance, package
rehash, and explicit reason model. Describe the lease precisely as a
verifier shared-output guard. For evidence intended to bind a complete release,
run the manifest in an owned detached worktree or equivalent immutable checkout
created at one clean commit, with outputs rooted inside that execution directory
and results copied out only after finalization. This also allows the reviewer
ledgers to remain intentionally dirty in the development checkout without
blocking an exact product-commit bundle.

Either rename the current bit to `provenanceEligible` or add a separate
`completeReleaseGateEligible` calculation that requires Release configuration,
no selected-step filter, all required manifest steps, and any declared manual/
hosted evidence policy. Keep lane/subset evidence useful without letting its
field name imply repository-wide completion.

**Tradeoff.** The live lease is cheap and fixes accidental local overlap, while
an owned worktree costs checkout/setup time and disk. Per-run build roots retain
parallelism but require every script/tool to honor them. Use the lease for normal
developer feedback and pay the isolated-checkout cost only for authoritative
release evidence; hosted CI already supplies that shape if its artifact is
retained immutably.

**Resolution evidence.** Preserve the new cross-process contention, normal
release, process-death release, and eligibility helper cases. Add wrapper-level
temporary-repository cases for final-provenance failure and scope eligibility.
Prove an authoritative run executes from an owned exact-commit worktree while
the primary checkout changes, and that its source/build/package inventory stays
bound to the owned revision. Then retain one clean complete Release run for the
exact product HEAD with every JUnit/build/package digest and immutable hosted or
archived evidence reference.

### EQ-028 — P1 — Host-owned crash recovery failure isolation and repair

**Status: Implemented at the code and automated-integration level by commits
`dc30be9`, `0ff403a`, `d171dc8`, `15dbeb0`, and DLV-001 commit `d0c0420`.
Packaged/manual recovery and a future scheduled clean integration checkpoint
remain verification evidence only. Installed-widget security is frozen under
the stabilization exit rule.**

**Evidence.** The first three commits establish a bounded host-private
write-ahead journal, bind every DACL snapshot to the opened volume/file identity,
and carry the catalog lease's retained object identities into runtime before
mutation. Recovery refuses pathname replacement and unintended broad or
alternate AppContainer authority rather than redirecting a stored descriptor to
a new object.

Commit `15dbeb0` replaces the global recovery slot with crash-atomic schema-3
records keyed by AppContainer profile while preserving legacy schema-2 recovery.
One global lock still serializes mutation and detects overlapping path/object
authority, but an unrecoverable record quarantines only its profile; disjoint
generations remain admissible. Focused Runtime cases cover distinct-profile
progress, overlap refusal, stale tokens, legacy ownership, arbitrary alternate
package SID refusal, and verified-clear behavior.

DLV-001 commit `d0c0420` supplies the missing operator path. Only the exact
trusted Settings worker receives the PID/nonce-authenticated private diagnostics
companion; the local CLI calls the same host-owned recovery service. Listing is
bounded to sanitized recovery ID/display/status plus an opaque exact token. The
Settings confirmation page never renders or speaks that token. There is no
force-clear, caller-selected path, SID, ACL, descriptor, or replacement-authority
input. Commit-versus-cancellation ownership retains the record when cancellation
wins and reports success when verified commit wins; stale, malformed, unavailable,
unverified, and unauthorized paths fail closed.

Focused grouped runs `20260810T030450Z-e56569f7` and
`20260810T030552Z-7e53affb` pass the final CLI/Settings/docs and
Runtime/diagnostics/Bridge boundaries. Stable dirty-worktree aggregate
`20260810T030727Z-449cac31` passes 41/41 with identical start/finish commit and
dirty fingerprint. It is correctly ineligible as clean release evidence. The
subsequent exact-commit attempt was interrupted before producing a retained
result and is not counted as pass or failure; the corrected verification cadence
moves clean product-wide proof to the next named integration checkpoint rather
than running the same six-minute aggregate twice for DLV-001.

**Why it matters.** A failed exact-object recovery no longer turns one package
anomaly into a machine-wide community-widget outage or asks a user to edit ACLs
or delete an internal journal. The product retains fail-closed authority while
providing one bounded, explainable remediation path.

**Tradeoff.** Profile-scoped records and an authenticated operator surface add
schema, transport, UI, and CLI machinery. They deliberately do not offer a
force-clear escape hatch: unrecoverable exact-object state remains quarantined
until verified restoration succeeds.

**Remaining evidence.** Exercise the packaged Settings flow against a real
interrupted AppContainer mutation and inspect residual ACE state; retain one
clean exact-commit product aggregate at the next scheduled integration
checkpoint. These are verification-queue items, not authorization to reopen the
subsystem or delay DLV-002.

### EQ-016 — P2 — Catalog recovery is implemented; aggregate discovery evidence remains incomplete

**Status: Control-plane recovery implemented in commit `8a46d5f`; partially
open for clean final-HEAD provenance, hard-wall-clock behavior, and
maximum-scale full-discovery cost. The reproduced enabled Spotify catalog has
been repaired without changing its selected generation.**

**Implementation evidence.** `WidgetCatalogOptions` now supplies defaults of
256 IDs, eight versions per ID, 512 total versions, 32,768 installed entries,
2 GiB accounted installed bytes, and a 30-second detected elapsed-work budget
checked around each version, while
retaining the per-version 512-entry/64 MiB bounds. Discovery caps top-level and
per-ID directory enumeration at N+1 before sorting, refuses a total-version
tail before verifying it, and accumulates verified entry/byte counts with
checked arithmetic. Each installed version conservatively accounts one
integrity metadata entry and its maximum 4 KiB rather than trusting current
metadata length. Installation runs under the existing operation lock and uses
the verified installed totals plus archive inspection to reject a prospective
ID, version, entry, or byte overflow before package publication.

The aggregate test family covers invalid option relationships, direct ID N+1,
prospective ID/per-ID-version/total-version/entry/byte refusal, an unexpected
root file, and a deterministic elapsed-time failure. It also asserts selected
install rejections do not create the incoming package directory. The
clean retained bundle `20260809T201448Z-0249ae81` executes the later expanded
Catalog suite at 32/32 with no failures for exact commit `0598e5a`. This review
did not rerun it. Its exact tree case
configures `MaximumInstalledEntries = 4`; the installed version already has
four filesystem entries (`manifest.json`, `.gbar-integrity.json`, the payload
directory, and the entrypoint), so adding one empty directory is the fifth and
correctly produces `integrity_limit`. Bridge 40/40, Settings 41/41, and the
49-file documentation contract are likewise implementation-reported green.

Commit `1c1f8bb` makes elapsed/cancellation detection fine-grained:
`CheckBudget` is passed into verification, recursive tree inspection invokes it
per entry, filename enumeration invokes it before materialization, and bounded
manifest/metadata/hash readers invoke it before every at-most-64-KiB read. A
deterministic reader case cancels between two chunks. Aggregate entry/byte
refusal still occurs after the one bounded version that crosses the limit has
been verified, and no cancellation token can preempt a synchronous Windows
filesystem read already blocked in the kernel. A future outer process watchdog
is still required for a hard wall-clock guarantee, but ordinary multi-file work
no longer waits for a complete 64 MiB version before observing the budget.

Commit `8a46d5f` implements the missing control plane. `InspectHealthAsync`
enumerates a separately bounded canonical ID/version directory projection
without opening candidate manifests. `RemoveInactiveVersionAsync` reacquires
and revalidates that projection under the catalog operation lock, protects the
selected generation, rejects reparse/path ambiguity and over-budget version
trees, checks cancellation before the atomic staging move, and does not expose
a recursive caller path or force mode. Settings provides a paged candidate list
and exact confirmation; `gbar repair list|remove` provides the same route when
normal discovery is unavailable. If one removal still leaves the catalog over
quota, Settings reloads the bounded recovery projection so cleanup can continue.

The exact policy defect from the prior worktree is fixed. `CanRemove` now means
`!Selected`, not `!WidgetEnabled && !Selected`, so inactive history remains
actionable while the selected version is enabled. Catalog, Settings, and CLI
tests start from enabled over-limit fixtures, corrupt an inactive candidate's
manifest to prove it is not trusted, protect the selected version, remove old
history, recover full discovery, and assert the enabled selected generation is
unchanged. Direct inspection of the product state after implementation finds
Spotify still enabled and pinned to 0.2.10 with only versions 0.2.8, 0.2.9, and
0.2.10 present; the former 19-version product-blocking state is gone.

Verification is strong but not release-complete. Dirty broad run
`20260809T215159Z-b62d551b` passes all 41 steps and 783 cases, including Catalog
34/34, Settings 42/42, and CLI 51/51 with the enabled-recovery test names and a
512-version repair measurement of 278.620 ms/1,711,440 allocated bytes. Its
manifest records base commit `b15075b`, a dirty worktree, and
`releaseEvidenceEligible: false`; the final enabling-policy edits were committed
afterward as `8a46d5f`. A second dirty broad run is not independent clean proof.
The implementation-status value of 326.382 ms is also not present in either
retained bundle inspected here. Run the final clean commit before calling the
evidence retained.

Recovery discoverability is weaker than the implementation. The quickstart,
authoring guide, and CLI README describe `gbar repair`, but neither
`docs/troubleshooting.md` nor `docs/diagnostics-and-recovery.md` contains
`installed_widget_version_limit`, `installed_version_limit`, or
`installed_widget_id_limit`. The compact Settings diagnostic shows only the
code; users must infer that opening Installed Widgets reveals the recovery
surface. Map each quota code to **Settings -> Installed widgets -> Catalog
recovery** and the exact CLI commands, and make the compact diagnostic expose a
controller-reachable recovery hint or action when that can be done without
duplicating catalog authority.

Commit `d2e49a9` discovery verifies and materializes every accepted version with a
complete relative-path/length/SHA-256 dictionary, not only the smaller GBSS map.
Only enabled active versions survive through bridge content-lease closures, so
the long-lived ownership is appropriately narrow; the discovery snapshot still
incurs the transient allocation for all versions. The hard quotas bound this
cost, but no cold/reload time or peak/transient-memory evidence exists at their
defaults. Measure before adding a second inventory representation or retaining
authorities for inactive history.

**Why it matters.** Catalog reload happens on the product's control path while
the overlay is in use. A large but individually valid catalog can cause long
bridge refreshes, allocation spikes, and delayed Settings recovery while the
user is gaming. Public sharing makes version accumulation normal rather than
an adversarial edge. Per-package safety claims therefore do not establish the
product's lightweight aggregate behavior.

**Underlying problem.** Resource budgets and recovery belong to the complete
catalog operation, not only each artifact. The catalog still combines health
inspection, security verification, active-version selection, cleanup authority,
UI listing, and publication evidence in one all-or-nothing materialization.
Hard failure is safe for publication, but it cannot also be the only route to
the control plane that repairs that failure.

**Recommended direction.** Keep the new product quotas, pre-publication
installer accounting, and fine-grained checkpoints. Name the remaining non-
preemptible local filesystem limitation honestly, and retain an overall
watchdog in the future bounded command/process runner rather than promising
that a cancellation token can interrupt every Windows filesystem stall.

Preserve the new separately bounded health projection, selected-generation
protection, enabled-history policy, exact operation-lock revalidation, and
manifest-free deletion authority. Extend recovery tests to multiple sequential
removals while discovery remains over quota, process restart between removals,
locked staging cleanup, maximum repair-ceiling refusal, and a stale Settings
confirmation after another catalog client changes the inventory. Keep the
current revalidation as the authority; do not cache a UI candidate as deletion
permission or add a broad `--force` recursive path.

Add an exact diagnostic-to-remediation contract. Documentation tests should
assert that all three public quota codes appear in troubleshooting/recovery
guidance beside the Settings route and `gbar repair list|remove`. A Settings
snapshot test should start from the compact failure presentation and prove a
controller user can reach the recovery list without knowing filesystem or CLI
details.

Separate lightweight version listing from publication evidence. Retain the
manifest/digest needed for review, but acquire and hold the full path/hash
inventory only for enabled active versions during bridge publication and again
for the future launch lease. If recomputation is chosen instead of retention,
measure it and keep equality with the reviewed digest explicit; do not trust a
stale cache merely to avoid hashing mutable files. Apply the bridge's enabled-
widget bound before expensive publication verification where possible.

**Tradeoff.** Small quotas simplify predictability but can make rollback-heavy
development annoying; lazy verification reduces normal startup cost but moves
failure to selection/enablement unless Settings preflights it. A persistent
digest cache is faster but is not authoritative without an immutable generation
or a file-identity/change-journal contract. Prefer bounded on-demand work and a
clear cleanup UX over an unverifiable cache.

**Resolution evidence.** Preserve N+1 cases for IDs, versions per ID, total
versions, aggregate files, and aggregate bytes, and add direct-discovery entry/
byte overflow plus a discovery-level deadline test across multiple files.
Preserve the deterministic per-buffer cancellation case. Prove
an over-limit legacy/external tree leaves Settings and CLI able to identify and
remove chosen safe package versions without manual deletion, after which full
discovery recovers. The enabled Spotify-style active-version case now exists;
retain it on exact clean HEAD and add sequential/restart/stale-confirmation
coverage. Prove the bridge does not retain disabled inactive GBSS
inventories merely to enforce its widget cap. Record cold and reload time plus
peak memory at the supported maximum, with several rollback versions per ID,
and retain the result as a release budget tied to the exact quota values.

### EQ-017 — P2 — GBSS file-source failures collapse into a false “missing” diagnostic

**Status: Resolved in commit `0d4eb80`; focused Release verification passes.
Installed integrity-state UX remains separately open under EQ-014.**

**Evidence.** The committed migration replaces
`IGbssSourceProvider.TryRead(path, out source)` with
`Read(path) -> GbssSourceReadResult`. `GbssFileSourceProvider` distinguishes
`Missing`, `UnsafePath`, `TooLarge`, `ChangedDuringRead`, `InvalidEncoding`,
`DigestMismatch`, and `IoUnavailable`; the loader maps them to stable bounded
diagnostic codes. Embedded Settings, in-memory tests, and the CLI entry-source
adapter have migrated. `gbar validate` now reads a real entry through the
bounded strict-UTF-8 provider instead of `File.ReadAllTextAsync`, and a focused
CLI case proves invalid UTF-8 reports `invalid_encoding`.

The Styling case now proves valid BOM handling and maps every closed status to
its expected diagnostic. It still uses a synthetic `ResultProvider` for most
loader mappings. Only invalid encoding is exercised through the CLI; no
end-to-end CLI/import cases currently prove `source_too_large`,
`source_changed`, `source_unavailable`, or a nested failure's safe source
location. Platform Settings changes one reparse case from `missing_import` to
`unsafe_import`. Installed digest mismatch reaches a `digest_mismatch`
compilation diagnostic, but `BridgeCatalog.LoadWithInstalledAsync` catches the
resulting `BridgeCatalogException` and publishes only “invalid styles”; it does
not raise a typed package-integrity state for Settings/retirement telemetry.

The initial public positional result briefly admitted contradictory states. The
committed revision corrects that before publication: construction is private,
`Status`/`Source` are read-only, and validated success/failure factories are the
only normal creation path. The latest loader revision also contains unexpected
third-party provider failures as `source_unavailable` while deliberately
allowing cancellation, out-of-memory, stack-overflow, and access-violation
conditions to propagate. A throwing-provider case proves its secret exception
text does not enter diagnostics.

**Why it matters.** Authors now receive the actionable reason that content was
rejected without leaking provider paths or exception details. The remaining
installed digest-race presentation is not information loss inside the styling
loader; it is a bridge/catalog integrity-state ownership gap tracked by EQ-014.

**Underlying problem.** The former boolean contract could not express the
security and resource policy enforced by file-backed providers. The closed
result and loader-owned mapping now preserve that information without making
filesystem exception text part of the public diagnostic contract.

**Recommended follow-up.** Add real file/import CLI cases for every feasible
rejection reason. Carry an
installed `DigestMismatch`/`ChangedDuringRead` through bridge catalog status as
package integrity evidence rather than only generic invalid-style copy; syntax
errors should remain widget-local style diagnostics. Keep raw exceptions and
filesystem paths contained at the provider boundary.

**Tradeoff.** The interface break is appropriate before 1.0 but requires every
custom/in-memory provider to migrate. Factory-only construction adds small
friction that protects external implementations from invalid states. Containing
non-fatal provider exceptions can hide programming errors unless diagnostics
remain observable, but it preserves the advertised compiler boundary. Treating
every I/O failure as package tampering would overstate
transient access problems, so only verified-content statuses should enter the
integrity path.

**Resolution evidence.** Styling 23/23 covers factory invariants, throwing-
provider redaction, every status-to-code mapping, hostile lengths, invalid
UTF-8, BOM input, and positive digest-bound imports. CLI 49/49 proves real-file
invalid UTF-8 reports `invalid_encoding`; Platform Settings 15/15 proves a
reparse-backed theme reports `unsafe_import`. Broader real-file/import coverage
and installed integrity-state routing remain valuable follow-up, but the false
`missing_import` abstraction defect is closed.

### EQ-018 — P2 — Retained captures prove renderer execution, not current visual quality

**Status: Open; the evidence pipeline is provenance-aware but has no current
state matrix or visual regression verdict.**

**Evidence.** The retained
`artifacts/evidence/auth-free/final-schema-v2-20260808-final/manifest.json`
honestly identifies a standalone widget-body harness, exact source/tool hashes,
and excluded pass criteria. It records a dirty source tree with 44 entries at
revision `f3ac48c`; the implementation HEAD at reassessment is `1c1f8bb`. Its
Spotify package is 0.1.6,
while the current manifest is 0.2.10. The 12 captures cover Games & Apps and
Spotify's initial/setup states only. Settings failed to start in the harness,
and YT Music, SDK Gallery, Spotify Player, Queue, Playlists, Devices, loading,
denied, retry, empty, maximum-page, and long-copy states are absent.

`Capture-OverlayEvidence.ps1` proves retained-file hashes, semantic snapshot
invariants, computed-style presence, process bounds, and zero renderer
diagnostics. It explicitly excludes golden image comparison and shell/window,
backdrop, tray/footer, transition, focus/input, compositor, and physical-
display fidelity. It records `rendererDiagnosticCount = 0` when the renderer
process exits successfully; it does not inspect layout bounds, clipping,
overlap, focus visibility, contrast, scroll reachability, or text truncation.
This review visually inspected four representative PNGs. The Spotify setup
compact and 150%-text views are legible, but that manual observation cannot
validate current playback surfaces or full-shell composition.

**Why it matters.** A professional UI can serialize, style, and render without
errors while still clipping translated content, hiding actions below a scroll
boundary, losing focus indication, wasting space, or becoming unreadable at
150% text. The most complicated widget's polished appearance is therefore
still asserted from old setup screens and semantic tests rather than current
representative product states.

**Underlying problem.** Artifact integrity, renderer execution, semantic
correctness, and visual acceptance are separate evidence classes, but only the
first three are automated. Authenticated-looking state fixtures are not part
of the public scenario workflow, so the hardest screens remain coupled to real
credentials or one-off test code.

**Recommended direction.** Build the credential-free scenario worker already
specified by the authoring roadmap and make first-party state fixtures ordinary
consumers of it. For each release candidate, produce one clean-HEAD bundle from
the actual packaged version and cover compact, standard, wide, 150% text,
reduced transparency, and high contrast across ready, loading, empty, denied,
retry/error, long-copy, and bounded-maximum collection states. Add deterministic
semantic layout checks for finite/in-viewport bounds, required focus cue,
reachable scroll endpoints, and non-overlap of declared controls. Use reviewed
tolerance-based image baselines only in a pinned renderer/toolchain lane, with
an explicit human approval artifact for intentional visual changes. Keep a
smaller physical full-shell/controller/mixed-DPI smoke separate from the stable
offscreen gate.

**Tradeoff.** Full-image hashes are fragile across fonts, drivers, and Windows
rendering revisions; avoiding them entirely leaves large regressions invisible.
A two-layer contract—deterministic geometry/semantic assertions everywhere and
tolerant images in one pinned lane—contains noise without treating visual QA as
subjective memory. State-fixture maintenance adds work, but also gives widget
authors the credential-free preview workflow the SDK currently lacks.

**Resolution evidence.** Retain a clean current-revision manifest whose package
versions match source manifests, with no unexplained harness gap. Prove all
required state/profile pairs have semantic snapshots, zero layout violations,
reviewed image results, and exact provenance. Add current full-shell captures
for dashboard, open widget, focus, reorder, failure, and transition endpoints,
then complete a physical controller and mixed-DPI/150% text smoke. A green
renderer exit or unchanged PNG digest alone is not closure.

### EQ-019 — P2 — The legacy Guide fallback polls four XInput slots every 25 ms while hidden

**Status: Open; the event-driven path is dormant when unused, but the no-device-
tracking fallback has no measured adaptive cadence.**

**Evidence.** `OverlayApp::Initialize` starts `kGuideCompatibilityTimer` with a
25 ms interval whenever `guideCompatibility_.Initialize()` succeeds but GameInput
device tracking is unavailable. When tracking works, the same timer is correctly
armed only while an Xbox-360-family device is connected and killed after the last
one disconnects. `HideOverlay` kills the 16 ms ordinary controller timer but does
not kill the Guide timer because that path must reopen the overlay. Every Guide
tick calls `XInputGuideCompatibility::PollRisingEdges`, which invokes the
dynamically resolved XInput state function once for each of four slots. The
retained dirty baseline observed 31.65 Guide timer messages per hidden second;
at four probes per message that is about 126.6 state calls per second on that
machine. The harness labels the counter as UI messages, not OS wakeups, and has
no Guide detection-latency metric.

**Why it matters.** The fallback can be active for an entire gaming session on a
machine where GameInput enumeration is unavailable, even when no legacy
controller is connected. A small CPU percentage from one process can conceal
frequent package wakeups and driver calls that compete with a game. Conversely,
blindly slowing the timer could make the system button feel unreliable, so the
right target is measured responsiveness per unit of background cost.

**Underlying problem.** Compatibility activation has a binary on/off policy but
no host-owned cadence/backoff model. Device discovery, connected-slot sampling,
hidden toggle latency, and visible input cadence are collapsed into one 25 ms UI
timer, and the production XInput function is not behind a deterministic policy
seam.

**Recommended direction.** Keep the event-driven GameInput registration as the
preferred path. For the fallback, introduce a small
`GuideCompatibilityPollingPolicy` that separates infrequent empty-slot discovery
from faster sampling of known connected slots and can choose hidden versus
visible cadence. Cache connected slots, back off empty-slot probes, and use
timer coalescing or an equivalent wait source so idle work does not force a
40 Hz UI-message stream. Select intervals from measurements on actual legacy
hardware—for example, choose the slowest hidden cadence that keeps Guide-to-
overlay p95 within an explicit 100 ms product budget—rather than encoding an
untested constant. Do not weaken the 150 ms duplicate-dispatch guard or ordinary
controller ownership semantics.

**Tradeoff.** Slower discovery can delay the first Guide press immediately after
a controller connects, while an extra thread/wait source can cost more than the
current timer. A two-rate policy keeps the implementation bounded: rare discovery
may be slower, known connected slots retain low latency, and modern GameInput
devices continue to use callbacks with no compatibility polling.

**Resolution evidence.** Add pure-policy tests for connect/disconnect, empty-slot
backoff, hidden/visible cadence, and duplicate-edge suppression with an injected
clock/XInput adapter. On hardware requiring the fallback, retain an ETW/WPR trace
for hidden idle and repeated Guide presses that reports scheduler wakeups, state-
probe rate, CPU, and end-to-end toggle latency. Compare the current 25 ms policy
with the proposed cadence over a long run and require the latency budget plus a
material wakeup/probe reduction. The existing UI-message counter alone is not
closure.

### EQ-008 — P2 — Focus persistence was scheduled from the steady paint path

**Status: Architecturally resolved in current HEAD; targeted performance and scheduling verification remain.**

**Evidence.** `OverlayApp::DrawWidget` no longer calls the resolver. The new
`ReconcileResponsiveFocusPersistence` path is invoked on `WM_SIZE`, snapshot or
presentation refresh, and focus restoration. Stable paints and animations no
longer traverse the immutable snapshot for this behavior. A reconciliation
still performs one O(nodes) traversal and constructs a candidate vector before
it knows whether the preferred node is visible or has a persistence key, but
that work is now tied to bounded state transitions instead of frame cadence.

**Why it matters.** The important product requirement is no continuing work
after settlement. Moving the scan out of paint removes the frame-cadence
multiplier and is the correct architectural fix. The residual transition cost
is unlikely to matter for bounded snapshots, but scheduling edges still need
proof so DPI, interface-scale, surface, and snapshot changes cannot miss a
required reconciliation.

**Underlying problem.** The scheduling problem is fixed. The resolver still
combines preferred-node discovery and candidate collection in one
allocation-bearing traversal, and the orchestration is embedded in
`OverlayApp` rather than exposed through a directly testable transition seam.

**Recommended direction.** Preserve the transition-owned scheduling. Add a
small orchestration test that enumerates resize, snapshot, presentation,
focus-restoration, DPI, and interface-scale changes and proves exactly when
reconciliation is required. Only optimize the resolver to a two-pass,
zero-allocation search if measurement shows the bounded transition cost is
material; do not reintroduce paint-time work or a fragile cache.

**Tradeoff.** Transition-owned scheduling adds invalidation edges that must stay
aligned with the renderer's responsive inputs. A two-pass zero-allocation
resolver would reduce each transition's work without adding cache state, but
the current bounded cost may already be negligible. Optimize from a focused
measurement rather than adding a cache whose invalidation is harder to prove
than the saved work.

**Resolution evidence.** A seam or counter should prove stable paints perform
no persistence scan while real compact/expanded and snapshot transitions still
preserve focus. Run the changed native tests and measure a worst-case bounded
snapshot during settled and transition frames. If transition allocation is
below budget, record that result and close this finding rather than optimizing
speculatively.

### EQ-009 — Disposition — Disabled navigation destinations remain focusable by contract

**Status: Not a defect; closed by source and focused-test audit.**

**Evidence.** The original finding assumed `isDisabled` and `isBusy` meant a
control was absent from controller navigation. The host deliberately separates
focus/navigation availability from activation availability. Declarative render
planning records focusable buttons, sliders, and action surfaces as navigable
even when disabled or busy; the hit-region enabled flag suppresses pointer and
activation behavior rather than semantic focus. Native renderer and surface-
focus tests explicitly retain exact focus on disabled and busy controls, and
controller-navigation tests keep directional traversal independent of
activation state.

`NavigationShell` therefore correctly keeps every destination in its explicit
wrap ring. A disabled destination remains reachable so its accessibility label,
unavailable cue, and explanatory content can be inspected. Its action cannot be
invoked. The same logical destination remains a valid responsive persistence
target, avoiding a resize-induced focus jump merely because its command is
temporarily unavailable.

**Disposition.** No code change is required. Filtering disabled or busy nodes
from the ring or persistence resolver would contradict the platform contract,
change focus ordering dynamically, and cause focus teleportation. Public docs
continue to state that disabled and busy controls remain focusable while
activation is suppressed. A physical-controller smoke remains useful release
evidence, but it is not evidence of a missing enabled-ring implementation.

## Product-readiness assessment

| Area | Current assessment | Principal remaining evidence |
| --- | --- | --- |
| Installed-widget isolation | Strong execution containment and digest-specific unsigned authority are retained. Commits through `d171dc8` bind verified catalog objects and DACL operations/recovery to opened file identities. `15dbeb0` isolates schema-3 recovery records by profile while keeping one conflict-checking mutation lock, refuses unintended alternate AppContainer authority, and lets disjoint generations proceed. DLV-001 `d0c0420` adds bounded exact-token Settings/CLI remediation with no force-clear or caller-selected authority. The final dirty patch passes both focused boundary groups and stable 41/41 aggregate `20260810T030727Z-449cac31` | **Verification evidence only:** packaged interrupted-mutation recovery with residual-ACE inspection and one clean exact-commit product aggregate at the next scheduled integration checkpoint; installed-widget implementation is frozen absent a reproducible P0 or threat-model violation |
| Installed catalog scale | Commit `8a46d5f` adds bounded manifest-free health plus exact-version Settings/CLI retirement, protects only the selected generation, and repairs inactive history while Spotify remains enabled; the real catalog is now down from 19 versions to three with 0.2.10 still enabled/selected. Dirty 41-step evidence exercises enabled recovery and a 512-version projection, but final HEAD lacks clean provenance and full discovery still eagerly verifies every accepted version | Retain a clean final-HEAD gate, add sequential/restart/stale-confirmation repair cases, measure cold/reload time and peak memory for full discovery at supported limits, and retain an outer watchdog |
| GBSS author diagnostics | Closed typed statuses remove false `missing_import` results, contain provider faults, and route CLI validation through the bounded reader | Add real file/import coverage for all statuses and surface installed integrity failures distinctly |
| SDK lifecycle/coordination | DLV-009 (`08d44db`, integrated by `304102a`) makes YT Music the second repeatable lifecycle/state proof: SDK Active lanes own auto-connect/progress/poll/Latest transport work, one immutable presentation record owns rendering, and no Task/CTS registry remains. DLV-030 (`549da57`, integrated by `6b9144d`) adds the responsibility proof through value-based connection, confirmation/rollback, action, and snapshot-only presentation seams. Spotify DLV-007 proves coherent keyed presentation; DLV-008 makes regions findable but remains one partial type, now dispositioned by DLV-043 | Document a narrow operation/responsibility migration recipe; keep domain policy private until another consumer proves a reusable public boundary; retain packaged churn evidence |
| Action dispatch | DLV-014 (`2a160b4`, integrated by `9060f12`) composes a deterministic YT Music post-admission failure through the real worker/runtime/bridge/native host into painted and polite UIA status. It proves exact generation/action/source, sanitized logging, focus retention, replacement/expiry, Hide/Stop, and no restart; native Release, bridge 47/47, and isolated addon acceptance passed | Retain physical GameInput and packaged assistive-technology evidence; do not reopen implementation unless those gates expose a concrete defect |
| Bridge scheduling | Accepted DLV-032 (`8c11a27` plus `9abdc6b`, integrated through `a92378a`) gives one typed dispatcher duplicate-ID, global-bound, per-widget FIFO, fatal-cancellation, cleanup, and two-second drain ownership. Deadline-expired work releases IDs/tails/slots but remains observed in quarantine; final Bridge passes 52/52 after retaining and correcting a predecessor-failure regression | Scheduling is closed. The shipping native client still cannot pipeline; DLV-033 owns its later asynchronous read/correlation boundary. The retained 1,312-line server's client/catalog/residency and diagnostics/recovery responsibilities are explicitly assigned to DLV-039 and DLV-040 |
| Bridge client lifecycle | Accepted DLV-039 (`57befdd`, `57f3e951`, `e1df09c`, and `a43efc7`, integrated through `2daae2a`) establishes the singular registry/notification owners. Accepted DLV-045 (`67df1d9`, integrated through `dfbe02d`) gives replies/events one complete-or-abort frame boundary. Accepted DLV-040 (`df304c3`, integrated by `7d5e29d`) reduces the server to 616 lines and moves bounded read-only diagnostics plus exact-token recovery policy behind focused projections; WidgetBridge passes 68/68 after the final source edit and docs pass 52/52. Public protocol and recovery authority are unchanged. | **Conditional cohesive exception at the accepted boundary:** retain the registry as the singular worker-generation/catalog/residency state owner and the server as the singular session/framing/routing/write owner. Reopen if diagnostics policy, another pipe/session/write or dispatcher policy, another independent coordination concern, or material unrelated growth enters either owner. |
| Managed capability broker | Accepted DLV-031 (`ffa1edc`, integrated by `27adec1`) reduces the broker authority owner from 2,377 lines to 837 while retaining singular identity, consent, lifecycle, lease, gesture, subscription, revocation, and event-sequence authority. Seven typed internal domain routes own value-based decoding, validation, backend execution, and projection | Retain the focused authority/domain tests as new capabilities arrive; do not reintroduce distant validators, a generic mediator, or public-protocol churn |
| Spotify platform provider | Accepted DLV-034 (`a5c80ac` plus `344ab48`, integrated through `a92378a`) reduces the singular identity/OAuth/vault/token/local-player owner from 1,724 lines to 1,032 and extracts endpoint, retry/rate-limit, and strict parsing seams. Provider 34/34 directly covers late canceled refresh non-publication and Disconnect session isolation; no second authority or public protocol appears | **Cohesive exception at the accepted boundary:** the retained owner exclusively holds package identity, OAuth/PKCE, vault/token-session replacement, local-player lifecycle, and provider-event authority. Preserve those singular responsibilities and reopen decomposition if another independently testable policy or material growth enters that owner. Live-account behavior remains manual evidence |
| Windows network provider | Accepted DLV-035 (`51a6ec4` plus `f9df9b9`, integrated by `d151173`) reduces the provider root from roughly 1,186 to 836 lines and retains one MTA lifecycle/native-adapter/committed-state/publication adapter over separate command, deadline/operation, reconciliation, event-projection, and bounded queue-admission owners. Accepted DLV-041 (`3e6d779` plus `4060f4b`, integrated by `4630866`) reduces the native adapter from 1,297 to 404 lines while retaining exactly one three-handle/three-callback/generation/publication/disposal lifetime owner over connectivity, WLAN, radio, and injected native-call policies. The correction closes recovery-versus-disposal, callback-publication, reentrant-disposal, and concurrent-disposer races. Final focused evidence passes provider 51/51, Platform Broker 51/51, and docs 52 | **Conditional cohesive exceptions at both accepted boundaries:** retain the provider's sole thread, adapter, committed snapshot, channels, and publication authority, and the adapter's sole native lifetime gate/handles/callbacks/generation/disposal authority. Reopen either boundary if policy or coordination returns to the provider, or another native lifetime, callback-registration owner, gate, or independent operation policy enters the adapter. Live hardware remains manual evidence. |
| Settings widget | Accepted DLV-036 (`fdcf5e7`) first moves main-page presentation, ordinary preference/navigation policy, and exact-token recovery policy out of the 2,872-line logical partial owner. Accepted DLV-044 (`8599694`, integrated through `316ecb8`) removes every production partial declaration and leaves one 1,133-line root over separate 221-line installed policy, 420-line installed presenter, 344-line permission policy, and 526-line permission presenter. The root retains the sole lifecycle, state lock, operation gate, service effects, committed state, and invalidation authority; policies/presenters own no host service, lock, task, cancellation, lifecycle, or invalidation state. Focused retained evidence passes Settings 53/53, SDK 84/84, Catalog 35/35, Broker 51/51, and docs 52 | **Cohesive exception at the accepted boundary:** retain the root as the singular settings/theme/catalog/consent/diagnostics effect adapter and committed-state transaction owner. Reopen decomposition if section presentation or selection policy returns, another independently testable service/lifecycle/coordination domain enters the root, or material unrelated growth occurs. The retained run is stable dirty assignment evidence, not clean release evidence |
| Managed worker client | Accepted DLV-037 (`b6a4de3` plus `d339030`, integrated by `8ea0fd5`) replaces the roughly 1,027-line multipurpose client with a 964-line singular host lifecycle/restart/failure/public-request adapter over one 352-line per-generation terminal session and focused request-correlation and gesture-reservation owners. The correction linearizes construction/Stop/resource transfer, exact current-session response/event/failure/gesture publication, and late cancellation-ignoring gesture revocation. Five manually controlled cases force the rejected races. Stable focused evidence passes Runtime 74/74, worker 9/9, Bridge 52/52, and docs 52; exact clean `d339030` evidence independently passes Bridge 52/52 after excluding preserved DLV-039 work | **Conditional cohesive exception at the accepted boundary:** retain the root as the sole public client/lifecycle/restart/failure adapter and the session as the sole generation transport/resource/publication terminal owner. Reopen if another lifecycle, session, publication, resource, request, or gesture owner appears, or material unrelated policy returns. DLV-039 and DLV-040 remain the separate bridge-server hotspot work |
| Responsive/controller UI | DLV-003 (`27b0319`, integrated by `703c5bb`) owns the narrow Button placement slice. Accepted DLV-021 (`b714efe`, integrated by `bc2de86`) adds one DirectWrite measurement/paint plan, final-width row remeasurement, outward pixel rounding, and exact Games/Spotify/Now Playing/Settings/SDK Gallery component profiles; focused counts are 25 text-layout, 250 layout, 4,769 renderer, and 589 shared-geometry checks. DLV-005 (`3fc3770`, integrated by `aaf36d9`) adds one host-owned 700 ms tray-Y recognizer. DLV-026 (`979de24`, `68efc70`, `9cc633a`, integrated by `4957101`) adds a scale-aware one-native-pixel reveal tolerance but failed the user's live four-session reverse-scroll check. Accepted DLV-049 (`32af19b`, integrated by `a8bcb27`) now locks the exact emitted Microphone Up edge to DLV-021's corrected geometry: Master remains offscreen-but-revealable at finite retained offset, one production HWND/UIA Up reaches Master and offset zero, reopening emits no `value_clamped`, and focused evidence passes 4,774 renderer plus 35 probe checks. DLV-020 (`b0c95ca`, integrated by `7cda335`) retains one admitted visual-only surface through destination startup, but its static production-HWND evidence did not predict real temporal behavior. DLV-025 then measured real populated first paints near 31 ms, six successful Spotify paints over 674 ms for 14 inputs, and five consecutive corrected captures with an exposed dark band. Removing repeated HWND interpolation was insufficient: resizing the current Direct2D HWND target exposes undefined content before successful draw, and a later `DwmFlush` cannot undo already composed frames | Keep GBA-003 Verifying until the fresh accepted Release passes the user's keyboard/controller reproduction. DLV-025 is Assigned as the user-authorized Windows-10-compatible DirectComposition surface gate; the Windows-11-only composition-swapchain path remains out of scope. Widgets-led serialized DLV-006 plus DLV-022 own continuous list/focus composition. GBA-058/GBA-060 remain Verifying pending the fresh packaged visual check. |
| Games & Apps | DLV-004 (`7e0b83e`, integrated by `76032bb`) accepts the shared responsive surface and bounded Catalog. DLV-017 (`24a8944` plus `b844fd8`, integrated by `5aedfe8`) adds bounded last-good projection and exact current-lifetime AppId admission. DLV-024 (`d80d9ec`, integrated by `6f401ea`) proves commit-before-publish removal, rollback/CAS continuity, stable focus, and a 600-DIP preferred height. DLV-027 (`df1dc81` plus `69e86ef`, integrated directly as `69e86ef`) then extracts pure presentation, bounded catalog policy, schema-v3 policy, and CAS storage while retaining singular lifecycle/action/state ownership. Accepted DLV-018 (`039b7b8`) plus DLV-054 (`546f014`), integrated through `8c1bbdf`, adds trusted executable artwork without growing the widget owner: revision-bearing handles rotate on provider revalidation changes, provider I/O is asynchronous to bridge control traffic, late retired-generation/session results are rejected, and the native cache/result queues remain bounded at 32. Focused Games passes 56/56, SDK 85/85, Bridge 70/70, Broker 51/51, Windows app-library 32/32, installed seam 6/6, renderer 4,777, and docs 54 | **Cohesive exception at the accepted widget boundary:** the 1,274-line root still retains only lifecycle, action admission, provider orchestration, and committed render-facing state; presentation, catalog navigation, schema policy, and CAS storage have focused value seams. Retain artwork I/O, registry, and cache authority in their accepted shared owners. Packaged/physical evidence remains |
| Network Controls | Accepted DLV-028 (`4ec931b`) reduces the root from 2,020 to 1,241 lines and moves provider normalization/reconciliation, command policy, action vocabulary, stable element identity, and complete presentation into value-based focused seams. The root remains the singular lifecycle, host-command, committed-state, and invalidation owner with one SDK-owned Active latest-operation lane | **Cohesive exception at the accepted boundary:** retain those four orchestration responsibilities together while the extracted policies remain state-free. Reopen decomposition if new provider policy, view composition, task ownership, or another mutable coordination domain returns to the root |
| Audio Mixer | Accepted DLV-029 (`9647718` plus `091ec51`, integrated through `6fc8d73`) moves complete snapshot-only presentation and closed output/input/session transitions into direct seams. Accepted DLV-042 (`37119f7` plus `0a3635a`, integrated through `f64c35a`) then moves linked lifetime, four subscription-before-snapshot paths/pumps, optional retry attempts, failure classification, and exact drain into one 484-line internal Active session. The root falls from 2,496 to roughly 1,687 lines, has no provider pump/retry semaphore/provider CTS/subscription startup, and admits only immutable observations from the exact current session. Accepted DLV-019 (`6afd60b`) adds LB/RB/X tray controls through the existing output command/reconciliation owner and exact broker gesture authority without another lifecycle, task registry, or committed-state owner. DLV-049 confirms the managed snapshot's exact Microphone-to-Master edge and closes the missing four-session production-host regression without changing managed widget state or adding a native fallback | **Cohesive exception at the accepted managed boundary:** the residual root is the single state-lock, action-admission, six host-control-call/command-task, committed-model, selection, status, and invalidation transaction owner. Keep the reverse-scroll issue Verifying until live keyboard/controller proof; do not reopen managed architecture unless that run contradicts the exact emitted edge. Treat endpoint selection as a separate supported-API/role-policy spike; do not use undocumented `PolicyConfig` behavior |
| Windows accessibility | Commits through `9ec0374` provide real composite UIA and physical-only origin enforcement. `6a079b6` mirrors managed Back semantics across focus/no-focus, disabled/busy, ancestor, stale, and nested-scope cases using allocation-free bounded recursion. Accepted DLV-015 (`b371983`, integrated by `6d3b093`) now composes representative Settings, YT Music, and Spotify production trees through one real HWND, `ProviderHost`, `WM_GETOBJECT`, and a UI Automation client. Its 183 assertions cover names/roles/help/values/bounds/order, hidden interactive exclusion, Invoke, RangeValue, focus, loading/error/selected/busy/disabled states, and generation focus restoration; five focused native groups, the Release host, packaged YT Music host/UIA fixture, and docs pass | Retain physical Narrator/MSAA and packaged AppContainer/UIA evidence; then extend the same real-client seam to Picker/ActionSheet/Navigator traversal when those product surfaces require it. |
| YT Music | DLV-009 (`08d44db`, integrated by `304102a`) removes three lifecycle task fields and the auto-connect flag and adopts SDK Active lanes plus one immutable presentation record. DLV-030 (`549da57`, integrated by `6b9144d`) reduces the owner from 1,365 to 677 physical lines, preserves singular lifecycle/client/state authority, and directly tests connection, confirmation/rollback, action, and pure presentation seams; retained focused evidence passes 55/55 plus SDK 84/84, worker 9/9, and docs 52 | Real companion, packaged lifecycle/controller/accessibility, and visual evidence remain; use the private seams as a reference, not yet a mandatory public framework |
| Spotify | DLV-007 (`ff706d2`) gives rendering one immutable keyed presentation revision. Accepted DLV-023 (`3cfdd27`, integrated by `4dc1bd5`) adds a widget-private typed transient/fatal policy. Accepted DLV-021 corrects shared Library-header measurement. Accepted DLV-051 (`dc22202`, integrated by `822d29c`) authors the inactive seek Slider Left edge to the selected wide rail destination or compact Player tab. Accepted DLV-022 (`c349bbd`) plus DLV-053 (`7f5c2fd`) supplies exact-intent continuous occurrence-aware lists, stale-generation rejection, exact keyed playback lookup, and the singleton graph correction, integrated through `8c1bbdf`. Accepted DLV-043 (`6c619e9`, integrated through `d534410`) replaces the 2,207-line/four-partial aggregate with one 1,275-line orchestration owner over value-only route/action and playback policies plus a snapshot-only presenter. Spotify 48/48, SDK 85/85, worker 6/6, and docs 54 pass. Accepted DLV-057 (`90cadf4`) isolates the complete package graph and makes warm main, repeated main, detached root, installed content, and planner refresh identical for selected `0.2.14` | **Conditional cohesive exception:** retain the root as sole lifecycle/provider-call/resource/task/committed-state/invalidation authority; reopen if policy/presentation or another coordination owner returns or material unrelated growth occurs. Exact main packaging is closed; PID 27684 is running for live list/focus/failure verification. Keep GBA-056/GBA-058/GBA-061 Verifying until live recurrence testing; run live auth/playback gates only when authorized |
| CLI author workflow | DLV-010 (`83cc32d`, integrated by `e68b8be`) provides a cloneable offline SDK dependency, generated lifecycle/state/action snapshot exporter, bounded source build/stage/validate/pack operation, deterministic checkout-path-free package proof, and local two-version install/select/rollback/removal. Accepted DLV-046 (`84ef91b`, integrated by `06f6cc6`) adds a strict versioned text/binary manifest plus all-or-nothing validated staging/publish. Accepted DLV-047 (`7e33f45`, integrated by `7da7eaa`) adds the checked-in package/API release unit; DLV-050 (`263536f`, integrated by `7563471`) gives its new compatibility surface 12 named MSTest.Sdk 4.3.2 cases and a separate bounded updater while legacy suites remain unchanged. Accepted DLV-048 (`829e9fd`, integrated by `18d461e`) compile-tests the exact starter and eight marked external author phases. Final mixed focused evidence passes compatibility 12/12, WidgetSdk 84/84, GbarCli 55/55, and docs 53 | Externally published/versioned SDK/template release, isolated semantic scenario execution, native preview, publisher provenance/signing, and automated update/CI evidence remain. Do not schedule a repo-wide test migration. |
| Performance | Per-worker Jobs plus aggregate admission and runtime-owned leases; active tickers are lifecycle-bound, `6fc9e01` aligns pack/install/runtime directory limits, clean retained selected exact-edge proof records 376.140 ms packing plus 2,528.883 ms through first validated render, and accepted DLV-032 bounds managed dispatch to 16 plus session drain to two seconds with manually controlled cancellation-ignoring quarantine proof; exact ACL application remains unbounded, the native client cannot use pipelining and synchronously blocks the UI, and the one-machine sample is not a production budget; hidden Guide fallback still polls at 25 ms | Enforce one full start budget and cancellable correlation-safe off-UI-thread native bridge I/O/responsiveness proof; adaptive Guide cadence with hardware latency/ETW evidence; repeated 1/8/many-widget churn and a clean GPU/wakeup gate |
| Visual evidence | Provenance-aware offscreen widget-body capture exists. DLV-024 retains 16 current widget-body captures and seven semantic snapshots for Games continuity, including compact, 150%, and wide removal states, with zero renderer diagnostics. DLV-021 retains 16 current Games/Spotify body renders and exact five-product production-renderer bounds while disclosing the Settings worker-start capture gap. DLV-020's 44 real-HWND frames removed startup blanking in its bounded harness, but DLV-025 real temporal evidence now proves the current HWND resize path still exposes repeated dark-band frames on list-heavy product surfaces. Historical user captures show the Library/alignment defects that DLV-021 now places in Verifying; Games & Apps artwork remains missing | Choose an atomic compositor architecture for DLV-025, then repeat timestamped first-party intervals. Run the fresh packaged DLV-021 visual check; retain clean current package/state/profile plus physical full-shell/controller/DPI evidence. Offscreen body captures cannot close compositor behavior. |
| Native host ownership | Proven low-level input, focus, lifecycle, bridge, renderer, singleton-process, and pinned-surface helpers. Accepted DLV-058 (`e160690`, integrated through `ae34f9a`) established the singular surface owner; accepted DLV-068 (`b83b3f7`, integrated through `9e795ac`) adds a separate 421-line pure placement/store policy while the `WidgetSurfaceCoordinator` aggregate grows from 653 to 964 physical lines. DLV-069 `ea2691c`, corrected by accepted DLV-073 `aaafefc` and integrated through `eef3162`, adds controller focus, pointer/UIA action composition, a bounded input queue, feedback, and emergency hide. A small pure mode policy now keeps admitted content constant while exposing interactive semantics only in Focusable mode; the opaque Click-through cover is gone. The coordinator remains 1,416 physical lines before and after DLV-073 with one admission/HWND/render/focus/input/placement/UIA/teardown owner and no new mutable dependency. Accepted DLV-070 (`c61a49d`, integrated through `0b21384`) isolates process election/activation before platform initialization. `OverlayApp` is about 5,500 physical lines and remains the mutable cross-subsystem hotspot. | **Conditional cohesive exception:** retain the coordinator as the singular composition owner for one host-owned pinned HWND while placement and presentation decisions remain pure policies. Reopen before accepting DLV-062 if media/session lifetime, a second renderer/focus/lifecycle authority, or material aggregate growth enters it. DLV-033 remains dependency-blocked for `OverlayApp` session-state extraction after DLV-025/DLV-032; do not let that internal refactor displace the active visible media gate. |
| Verification gate | Clean release-eligible run `20260809T201448Z-0249ae81` remains the last exact full proof: 41/41 and 778 cases for `0598e5a`. Commit `7c8a5b8` adds a live verifier lease, final commit/status, final package hashes, and typed reasons. DLV-001's final stable dirty run `20260810T030727Z-449cac31` passes 41/41 with identical endpoints/fingerprint but is correctly ineligible; its later clean attempt was interrupted before producing a result. The delivery plan now prohibits repeating the same aggregate dirty and clean and names DLV-004/DLV-006 as the next exact-commit checkpoints | **Verification evidence only:** retain one authoritative bundle at the next named checkpoint; do not let provenance cleanup displace DLV-002/003 or cause duplicate six-minute runs |
| Independent review ownership | Documentation commit `689a933` stages both review ledgers from the implementation stream and self-closes EQ-021 without retained evidence, mixing implementation, reviewer authorship, and disposition | Reserve both review files to the reviewer; require path-specific implementation staging and evidence proposals through implementation status rather than self-edited closure |
| Documentation | Extensive, and current accessibility pages now consistently distinguish implemented preview from the ship gate; green contract checks still link-check rather than compile roughly 70 C# fences, and `plugin-platform.md` overstates generated test/replay support | Compile-test canonical snippets, derive overview claims from generated-template end-to-end tests, add semantic cross-document assertions for product-status claims, bind status claims to exact result manifests, and reduce ledger/status duplication |

## 2026-08-11 accepted integration delta

### Returned candidate review — DLV-114 and DLV-112

**Accepted correction disposition (2026-08-11).** DLV-117 `6a96727` closes the
DLV-114 UX/correctness gap without adding another task, lock, timer, provider,
or committed-state owner: first selection is named as a distinct-game step,
second selection names the exact group/removal operation, and stale/same-ID
cases fail closed. DLV-116 `0e1810f` closes the DLV-112 integration/evidence
gaps with one exact bundled-Settings action admission seam, exact submitted-
operation ownership, Release-hard framing failures, and the missing ownership
record. DLV-115 `5c66f16` is also accepted for packaged live verification: it
retires interrupted composition geometry at logical hide and proves no hidden
frames before atomic transparent reopen. The corrected prefixes are integrated
through `cff0d99`; the returned findings below remain as historical review
rationale, not open rejection.

- **DLV-114 candidate `f285c9d` is returned for DLV-117.** The change keeps
  exact SavedId selection, disappearing-identity refusal, Back focus/scroll
  restoration, and snapshot-only details policy/presentation outside the
  lifecycle owner. `GameLauncherWidget` grows from 1,140 to 1,207 physical
  lines but gains only one immutable selection reference and route/action
  adaptation—no task, lock, timer, resource, provider, or second committed-
  state owner. The visible action contract is not yet acceptable: the details
  page labels one activation **Group variant** or **Ungroup variant**, while the
  reused implementation starts or completes a two-distinct-game sequence, and
  the details page does not render the resulting status. DLV-117 must make that
  interaction honest and add exact first/second/removal evidence before the
  contiguous prefix can integrate.

- **DLV-112 candidate `1dd2dd5` is returned for DLV-116 after active DLV-115.**
  The separate native picker, managed single-flight install service, exact
  current bundled-Settings admission, reparse-resistant locked source stream,
  catalog-owned pre-publish policy, disabled publication, and bounded path-free
  result queue are credible ownership boundaries. The production prerequisite
  is incomplete because `OverlayApp::BeginLocalWidgetPackageImport` has no
  caller or closed action-admission seam, while DLV-113 is widgets-only and
  cannot add native work. The new completion-event assertions in
  `WidgetBridgeCatalogTests` also use `assert` in a Release `/DNDEBUG` build, so
  they execute no checks, and the required implementation-status responsibility
  record is missing. DLV-116 must close those exact gaps without reopening
  package policy or exposing a generic widget/file capability.

- **DLV-033 (`1ec2b70`, integrated through `4fa8f63`)** moves descriptor,
  snapshot, typed failure, lifecycle-target, catalog-retry, generation, and
  bounded request-queue ownership from `OverlayApp` into one native
  `WidgetSessionCoordinator`. `OverlayApp` drops the named collections and 167
  physical lines while remaining the only Win32, renderer, focus, input,
  accessibility, and presentation authority. Focused evidence passes six
  coordinator scenarios plus state/lifecycle/failure and retained-content
  groups. The stall evidence proves UI-thread Close/Guide intent and retained
  neighbor presentation remain responsive; it is not parallel bridge-request
  or pipelining evidence.

- **DLV-110 (`e90e729`, integrated through `4e02184`)** replaces the single
  trivial scaffold with basic, data, media, and multipage profiles over the
  existing public SDK. The strict versioned inventory stays transactional; each
  generated external project uses `MSTest.Sdk` 4.3.2, isolated semantic preview,
  validation, and deterministic packaging without repository project
  references. Agent evidence passes Gbar CLI 57/57, generated scenarios 9/9,
  compatibility 12/12, and documentation 58 files. This does not migrate legacy
  test runners or claim an externally published SDK release.

- **DLV-107 (`b662e9a`, integrated through `0e0bf77`)** replaces the rejected
  mixed color-key/composition-alpha path with one fixed transparent HWND
  container, alpha-zero premultiplied clear, one composition effect opacity
  owner, and transform-only extent motion. `OverlayCompositionSurface` owns the
  DComp device/surface/effect primitive and `PlanCompositionMotion` owns pure
  geometry; `OverlayApp` still coordinates admission, Win32 placement, input,
  focus, UIA, and fallback. The change is accepted for live verification, but
  its roughly 470-line `OverlayApp` growth exhausts the existing hotspot's
  cohesive exception rather than closing it.
- **DLV-033 is now Assigned to the platform lane** while widgets delivers the
  visible DLV-110 authoring outcome. It must extract descriptor/snapshot,
  generation, retry, lifecycle-target, and bounded request-completion ownership
  into one native `WidgetSessionCoordinator`, materially shrink `OverlayApp`,
  and prove a before/after responsibility map. No second renderer, focus, input,
  HWND, presentation, or generic event authority is acceptable. Another
  internal-only platform milestone may not follow it.
- **DLV-102 (`e4f9880`, integrated through `0e0bf77`)** keeps terminal trusted
  artwork in the bounded cache, uses the existing actionable shared tile
  fallback, and deduplicates diagnostics at state transition rather than paint.
  It adds no provider, protocol, or widget-specific artwork authority.
- **DLV-108/109 (`f51a983`, `5db3426`, integrated through `bc484f0`)** establish
  isolated named semantic execution plus an optional renderer-neutral scenario
  test API. Author code never executes in the CLI, MSTest.Sdk 4.3.2 adoption
  remains limited to the new project, and existing executable suites are not
  migrated.
- The coherent main Release refresh passed the complete native host build and
  full managed/runtime packaging. The first full combined invocation recorded
  one timing-sensitive `WidgetActionFailureHostTests` dashboard-republish
  failure; the exact focused command then passed once. That rerun is
  classification evidence, not a fix or a clean Tier-3 claim. PID 32508 is the
  visibly running accepted package for user verification.

## Security stabilization exit rule

Security work must now converge on a bounded release gate rather than expand
indefinitely through speculative defense in depth. The relevant threat boundary
is an untrusted community package and its capability-free AppContainer worker
attempting to influence host/broker authority, package bytes, another package,
or the desktop user through supported product entrypoints. A compromised
desktop account, administrator/kernel control, or arbitrary same-user software
already able to rewrite the installed application is not a reason to keep the
widget framework's current product milestones blocked unless it produces a
concrete cross-boundary exploit under the documented model.

Treat installed-widget security stabilization as complete for the current
product phase when all of the following are proven together:

1. Broker calls remain authenticated, declaration/consent/lifecycle bounded,
   typed, and inaccessible through raw handles or ambient worker authority.
2. The worker launches only from the verified package generation; content bytes,
   namespace, and DACL targets remain bound through process creation and reject
   replacement or unintended broad/alternate AppContainer read authority.
3. Partial ACL mutation and host termination recover through host-owned durable
   state; one unrecoverable package is diagnosed and repaired or quarantined
   without silently disabling unrelated packages.
4. Corrupt/missing recovery state fails closed with an explicit user-facing
   remediation path rather than asking users or authors to edit ACLs or internal
   journal files.
5. Focused boundary suites and one stable product aggregate exercise these
   invariants; the next scheduled exact-commit integration checkpoint plus the
   real AppContainer/package probe retains release-quality evidence without
   repeating the same full gate before and after every commit.

Commits through DLV-001 `d0c0420` satisfy the code-level stabilization gate.
The interrupted exact-commit attempt produced no result; clean product-wide and
real packaged recovery evidence therefore remain in the verification queue.
That evidence debt does not reopen implementation or delay DLV-002.

After this gate passes, freeze the subsystem. New security work should displace
product, SDK, UX, or performance milestones only for a reproducible P0, a
concrete violation of this threat boundary, or an explicit requirement for the
next public-distribution stage. Other same-user hardening, unusual filesystem
permutations, additional cryptographic provenance, and defense-in-depth ideas
belong in the later security backlog with P2/P3 priority. This rule does not
claim that security is permanently complete; it prevents the absence of such a
claim from becoming an unlimited implementation program.

## Recommended next actions

Continue enforcing EQ-024's path-ownership rule: `ddb66c2`, `7c8a5b8`,
`0be052b`, `dc30be9`, `0ff403a`, and `d171dc8` correctly left both review
documents untouched, and the implementation stream must continue to leave them
unstaged. This workflow boundary does not replace the visible-first sequence
below.

1. **Live-check accepted DLV-049 and DLV-051.** DLV-049 is integrated as
   `a8bcb27` and binds the exact four-session reverse edge to DLV-021's corrected
   geometry; DLV-051 is integrated as `822d29c` and authors Spotify seek Left to
   the selected responsive destination. Keep GBA-003/GBA-056 Verifying until the
   freshly rebuilt Release passes the user's direct keyboard/controller checks.
2. **Verify accepted widgets DLV-019 physically.** Commit `6afd60b` delivers
   LB/RB five-point master-volume adjustment and X master mute through exact
   dashboard-gesture authority. The installed production route is green; keep
   GBA-062 Verifying until the freshly launched Release overlay passes the
   physical-controller step, rapid-input, mute, failure, and reopen checks.
3. **Live-check the accepted visible widgets sequence, then close package
   reproducibility.** DLV-006 is accepted through `9c7438f`; DLV-022, DLV-018,
   DLV-053, and DLV-054 are coherently integrated through `8c1bbdf`. Accepted
   DLV-055 `efffa53` packages, installs, selects, and first-renders the latest
   Spotify source as `0.2.12`; PID 25956 is visibly running for the user's direct
   list/header/lifecycle and Games-artwork checks. Accepted DLV-043 `6c619e9`
   is integrated through `d534410` and materially closes the partial-type
   ownership gap. Accepted DLV-057 now proves identical warm-main/repeated-main/
   detached-root/installed/planner-refresh hashes for main-built `0.2.14`
   without deleting older immutable generations. Run the visible overlay and
   keep any new live defect ahead of internal cleanup.
4. **Deliver the next requested visible platform and widget foundations.**
   Platform DLV-058/068/069 ships generic pinning lifecycle, placement, and
   input/UIA composition before DLV-062's fixed-video feasibility gate. Widgets
   DLV-059 directly unlocks DLV-060's installed-only Game Launcher, followed by
   explicit organization and honest launch-state slices. Keep discovery/cache/
   launch authority in the trusted normalized service, not the widget.
5. **Preserve DLV-025 until the compositor decision is authorized.** The
   current HWND render-target path failed the real temporal gate. Keep its dirty
   evidence untouched while other visible work proceeds; resume only with a
   bounded offscreen atomic-present or DirectComposition/swap-chain decision.
6. **Keep internal debt dispositioned, not dominant.** DLV-040, DLV-043, and
   DLV-038 remain valid deferred ownership/test-architecture work. Promote at
   most one while the other lane is delivering visible behavior or when it is
   the immediate named prerequisite for a visible outcome. Hotspot size alone
   does not outrank a reproduced product bug.

**Later backlog.** Audio endpoint switching should begin with a supported
Windows API/role-policy spike rather than undocumented `PolicyConfig` behavior.
Spotify's composed live paging proof, the advanced-widget reference
architecture, native session extraction, accessibility completion, and
performance baselines remain valuable, but they should be scheduled as named
milestones rather than allowed to interrupt the visible outcomes above.

**Verification evidence only.** DLV-004 retained its named focused exact-commit
and capture evidence without repeating the six-minute aggregate. The next full
gate remains only at the next explicitly named Tier-3 checkpoint; do not run it
to decorate unrelated provenance.

The next review should rotate through UI/UX and widget authoring first. Revisit
installed-widget security only for a reproducible threat-boundary regression,
P0, or planned-release blocker. If no product milestone lands
after the minimum interval, report no material change rather than manufacturing
another finding.
