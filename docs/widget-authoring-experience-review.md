# Widget Authoring Experience Review

Status: living assessment; core coordination primitives, bounded route navigation, responsive focus persistence, one navigation recipe, unified managed action admission, bounded composed native action-failure presentation, data-only inspection, a truthful local-SDK scaffold, manual GitHub package lifecycle, pre-routed focus-edge paging, a composite open-widget UI Automation preview, explicit input origin, collision-proof composite identity, focus-aware allocation-free nested Back, bounded enabled-history catalog recovery, inner physical-only gesture enforcement, and one coherent advanced-widget presentation state are implemented; real advanced-widget failure-route proof, real-host accessibility proof, shared native/managed Back ownership, a composed/live Spotify 12/12/5 route, final clean evidence, verified publisher trust, automated update discovery, a published standalone SDK/test scaffold, isolated semantic preview execution, broader recipes, and onboarding remain open<br>
Date: 2026-08-09<br>
Reassessed: 2026-08-09 against integrated `main` `aaf36d9`, including accepted DLV-008 Spotify responsibility boundaries and accepted DLV-005 tray hold refresh, their focused Release evidence, stable dirty full run `20260810T030727Z-449cac31`, the retained clean full-gate result for `0598e5a`, and the two-lane delivery plan<br>
Scope: public widget authoring APIs, tooling, examples, and the complexity exposed by advanced widgets such as Spotify

Related: [Engineering Quality Review](engineering-quality-review.md) covers the
cross-cutting architecture, security, verification, and product-readiness
findings that qualify this authoring assessment.

Review ownership: finding status in this document is maintained by the
independent reviewer. Documentation commit `689a933` was produced by the
implementation stream and self-declared the action contract complete; this
reassessment restores the evidence-based partial status. EQ-024 requires future
implementation commits to leave both review documents untouched and unstaged.

## Executive conclusion

The framework has a sound foundation: a native host owns rendering, focus,
accessibility, permissions, and lifecycle, while isolated C# workers contribute
renderer-neutral UI and react to typed actions. Simple widgets are already easy
to express.

The current cycle is **stronger as an advanced-widget product proof, but still
flat for external onboarding**. DLV-001 adds no community authority, and DLV-002 deliberately
implements Games & Apps curation as widget-owned versioned private state over a
bounded trusted capability. Its pure reconciliation type, explicit automatic
provenance/exclusions, deterministic lifecycle races, and credential-free
real-package fixture are useful patterns, but they do not yet simplify the
public multi-page/remote-state authoring path. Spotify now has one coherent
presentation revision, an explicitly keyed playlist-detail owner, and accepted
named responsibility boundaries. DLV-009 makes YT Music the second lifecycle
proof, and the external scaffold/package journey receives its own bounded
milestone while native geometry, feedback, accessibility, and performance work
continues independently.

The framework becomes difficult when a widget has multiple pages, remote state,
commands, caching, optimistic updates, and lifecycle-sensitive work. Authors
currently implement too much coordination infrastructure themselves:

- locks and state snapshots;
- task and cancellation-token ownership;
- single-flight, serial, and replace-running operation policies;
- loading, stale-result, retry, and cached-data states;
- optimistic updates and rollback;
- route stacks, Back handling, and focus restoration;
- responsive composition outside the new navigation-shell recipe; and
- extensive manual invalidation.

This is not unique to Spotify. Similar patterns remain in Audio Mixer, Network
Controls, Games & Apps, and YT Music. Media Sessions now demonstrates that the
model/command primitives can remove most handwritten synchronization from a
real widget, but the larger samples show an adoption and composition gap: the
helpers exist without one advanced reference architecture that teaches how to
combine them. The framework can express advanced widgets, but some safe
implementation patterns still require broader migration and recipes.

The recommended direction is evolutionary, not a rewrite: retain `Widget`, the
declarative protocol, GBSS, AppContainer isolation, and typed capabilities.
Add optional high-level SDK primitives that encode the safe patterns already
implemented repeatedly by first-party widgets.

The Spotify and SDK Gallery work support this direction, but a live Spotify run
now exposes a missing integration contract. Protocol-v11 host-owned focus-edge
pagination and `ScrollElement.Paginate` move generic page metadata out of
Spotify and into the platform; the native host does not yet compose boundary
routing and page-entry focus atomically. The public
`WidgetOperations` coordinator now also owns bounded SingleFlight, Latest, and
Serial execution plus lifecycle cancellation/draining. The public
`WidgetPagedResource<TItem>` builds on both contracts with offset-page state,
validation, invalidation, a deterministic bounded LRU, retry, and entering-edge
focus. Spotify has migrated its playlist collections to that API. The public
`WidgetModel<TState>` now serializes immutable state transitions, suppresses
equal-state invalidations, and can derive an operation input from the exact
committed revision. Media Sessions now keeps all render-facing state in one
model and uses the public `WidgetOptimisticCommand` coordinator for transport.
The public `WidgetResource<TValue>` now coordinates non-paged reads, while
`WidgetNavigator<TRoute>` and `WidgetIds` own bounded route history, route
cancellation, exact-scope Back, focus restoration, and validated hierarchical
IDs. `UI.NavigationShell` now renders one destination model as compact tabs or
an expanded rail around one shared content subtree, with optional expanded-only
content. Protocol v13 now separates an explicit bounded focus-persistence
identity from element and action IDs; the shell emits one shared key only for a
destination's compact and rail presentations. SDK Gallery is the production-
style reference for the composition, and accepted DLV-009 moves YT Music
auto-connect, progress, polling, and transport reconciliation into SDK Active
operation lanes around one immutable presentation revision.
Cursor/append resources, additional page recipes, an ID/action analyzer, and
broader migrations remain open.

The first scenario-tooling slice is deliberately manifest-only. `gbar preview`
can validate and list bounded manifest-declared states without resolving or
loading provider assemblies. Selecting a scenario fails closed until execution
can move behind an isolated, forcibly terminable process boundary. Semantic
snapshot execution, interaction, viewport rendering, capture, fake-service
lifecycle support, and template integration remain open.

`gbar render` is now also strictly data-only. It rejects DLL input before
resolving a type or touching an output path, closing the full-trust author-code
escape and making `gbar dev` the only executable CLI integration path. An
initial path-level 4 MiB preflight was racy; the current source now reads one
restrictively shared stream through a ceiling-plus-one detector and rejects
legacy assembly-only options for JSON. A deterministic misreported-length test
covers the consumed-byte bound. Clean all-lane result
`20260809T141527Z-8946c731` now retains the passing CLI step as part of 41/41
manifest steps and 755 JUnit cases for exact commit `dc6b092`; this review
inspected the retained result rather than launching it.
Current clean selected run `20260809T152831Z-67b77c73` also retains CLI 49/49
for documentation-only HEAD `6bd60d3` over implementation baseline `6fc9e01`.
It selected seven launch/package-oriented steps, so it is current CLI evidence
but not a complete current 41-step gate.
The broader tradeoff is an explicit tooling gap: there is no headless isolated
command that turns widget/scenario code into a snapshot. Authors must currently
add an author-controlled typed-fake test to persist `SnapshotJson` or use the
interactive overlay path.

The scaffold is now an honest repository-contributor path, not yet a standalone
community authoring product. Inside this checkout, `gbar new widget` discovers
`WidgetSdk.csproj`; elsewhere it requires an explicit validated `--sdk-project`
and fails before writing when none is available. A focused unrelated-directory
test builds that explicit local-project result. The generated README also uses
`gbar dev` instead of rejected DLL rendering and teaches bounded idle unload.
That fail-fast/override behavior is the correct interim contract until a
matching versioned SDK/template release exists. It is still not a cloneable
standalone dependency, and the new snapshot guidance needs an actual generated
typed-fake test/exporter; the template currently refers to one but creates
none. It also lacks a complete package path: the manifest expects
`payload/<WidgetName>.dll`, ordinary build writes under `bin`, validation does
not check that file, and the template never stages it for `gbar pack`. The
implementation agent reports 49/49 CLI cases; this review inspected the new
external build case, and the clean all-lane bundle now retains its passing CLI
result. That case still points back to the checkout SDK, executes only the
build, and proves neither a published dependency nor the advertised package
workflow.

The presentation layer is further along than an earlier gap list implied.
Pressed-state delivery, bounded subtree translation, responsive branches and
Grid, row wrapping, per-edge borders, semantic code text, activate-to-adjust
Sliders, and the modern controller component set are implemented. They should
be treated as current authoring tools, not roadmap proposals. Higher-level page
recipes beyond the navigation shell and packaged-font support remain distinct
open work.

The committed GBSS source migration is a meaningful pre-1.0 authoring
improvement.
`IGbssSourceProvider.Read` now returns `GbssSourceReadResult`; the loader maps
missing, unsafe, oversized, changing, invalidly encoded, digest-mismatched, and
unavailable input to distinct bounded diagnostics without exposing raw
exceptions or paths. `gbar validate` routes file entries and imports through
the same bounded strict reader. Implementation-reported Styling 23/23 includes
the complete status map, UTF-8 BOM, verified imports, and hostile-length cases;
CLI 49/49 adds one real-file `invalid_encoding` case.

The result is factory-only with read-only state, correcting the initial
positional-record hole before publication. The loader contains non-fatal
custom-provider exceptions as sanitized
`source_unavailable` diagnostics while preserving cancellation and process-
fatal conditions. Add real CLI/import coverage beyond invalid encoding, and
route installed digest/change rejection to typed catalog integrity status
rather than only “invalid styles.”

Commit `d2e49a9` makes a major platform-internal authoring improvement:
catalog discovery now captures path, length, and SHA-256 evidence for every
package file, and each installed-worker start reacquires a host-owned content
lease that pins those bytes and grants the AppContainer only direct,
non-inheriting access to the verified directory/file set. Authors still declare
an entrypoint and package ordinary dependencies/assets; they do not compute
hashes, manage ACLs, or write loader guards. That is the right ergonomic
boundary. A new runtime test already runs a real AppContainer worker, replaces a
prior broad grant for the same SID on the current root, and proves a verified
text file remains readable while a late file is denied. Treat the complete
workflow as pre-publication until the remaining adversarial cases pass. Clean
eligible run `20260809T152831Z-67b77c73` retains the selected Release path for
documentation-only HEAD `6bd60d3` over implementation baseline `6fc9e01`. The first-party
conformance harness now routes five real installed packages through the exact
lease, including YT Music suspend, restart, force reload, update, and removal.
The runtime now rejects content authority overlapping the trusted generic-worker
directory and preserves caller cancellation during content acquisition. Commit
`dc30be9` also replaces the AppContainer-writable quarantine marker with a host-
private, reparse-rejecting, globally locked write-ahead journal established and
flushed before DACL mutation. A real terminated child proves the next host
restores pending authority. Commit `0ff403a` binds snapshots and ACL work to
volume/file identity, retains target handles through apply/verify/rollback, and
rejects a replacement object during recovery. Dirty full run
`20260810T005210Z-c46e6881` passes 41/41 steps and 793 cases, including Runtime
58/58, but is correctly release-ineligible.

This remains the right authoring boundary: package authors still supply ordinary
dependencies/assets and never manage hashes, ACLs, object IDs, or quarantine.
The remaining product-workflow defect is blast radius and recovery. One global
pending record that cannot recover currently prevents every community widget
from starting, while Settings, diagnostics, and CLI provide no supported inspect
or verified repair operation. An author must not be asked to tell users to
delete an internal journal or edit ACLs. Keep the global mutation lock, isolate
quarantine to the affected profile/generation where safe, surface a sanitized
host-owned recovery state, and clear it only after exact-object or intended-SID
authority is verified. The alternate-authority audit must also cover specific
other AppContainer SIDs, not only the two broad well-known groups. These are host
obligations, not new SDK concepts or manifest fields.

The launch path is not yet performance-complete. The documented five-second
admission deadline covers content revalidation, but exact AppContainer ACL
application happens afterward with no shared deadline. A new 512-file test
accepts any complete startup below ten seconds and does not retain the measured
phase timings. Authors should not have to learn an undocumented “keep your
package tiny or the overlay may freeze” rule. The platform should enforce one
start budget, expose a typed package/start failure, and publish measured package
entry guidance; if per-file ACL mutation cannot meet the product budget, the
host should change its protected-generation design or documented file limit
rather than pushing ACL/performance workarounds into widget repositories.
This is also a host-liveness issue: the native bridge client currently performs
synchronous, untimed reads from overlay UI paths, so a stalled managed start can
prevent the overlay from painting an error or accepting its close/navigation
controls.

Commit `6fc9e01` closes the deterministic package-shape mismatch. Package
inspection and installed-tree verification compute the same canonical
package-root/implicit-directory set and reject more than 1,024 directories
before extraction/publication or launch. Bridge coverage keeps that catalog
limit aligned with the runtime outer guard, the public packaging guide publishes
it, and a within-file-limit deep-path fixture proves early refusal. Commit
`4f903b0` materially closes the remaining local edge proof:
an exactly 1,024-directory,
258-file package uses public pack/install/enable and renders a real first-party
widget through the production AppContainer path. The paired 1,025-
directory CLI case leaves no pack output and publishes no installed bytes.
Authors no longer discover the hard limit only on first open.
Clean selected run `20260809T152831Z-67b77c73` retains the negative admission
path through Catalog 32/32, Bridge 42/42, and CLI 49/49. The new focused CLI
50/50 and First-Party Conformance 6/6 results add the accepted and atomic-
rejection edge cases. Clean retained selected run
`20260809T155221Z-ae6e5d8d` independently passes those suites plus the
documentation contract (57/57 cases total) in 95.533 seconds and records
376.140 ms packing plus 2,528.883 ms through first validated render. It is
release-evidence eligible for clean documentation commit `b2956ab` over
implementation `4f903b0`, with zero stderr/truncation. Its three selected steps
do not update unrelated verification lanes.

## Evidence from the repository

Approximate implementation sizes in current HEAD illustrate the gap
between a minimal and an application-like widget:

| Widget | Approximate C# size | What it demonstrates |
| --- | ---: | --- |
| Clock sample | 40 lines | Deterministic render and one action |
| SDK Gallery | About 400 lines | Public components, navigation, responsive shell, and local state |
| Recent Apps | 340 lines | One event-driven provider surface |
| Media Sessions | About 770 lines | Selection, commands, progress, and provider lifecycle |
| Games & Apps | 1,150 lines | Navigation, paging, private state, and launch commands |
| YT Music | About 1,365 lines | SDK-owned lifecycle lanes, one immutable presentation revision, and authored companion confirmation policy |
| Spotify | About 1,950 lines across four responsibility files | Lifecycle/action wiring and singular state/resource ownership; route data; playback behavior; snapshot-only presentation over four destinations |
| Network Controls | About 2,000 lines | Multiple providers, discovery, commands, and failure states |
| Audio Mixer | About 2,500 lines | Dense state reconciliation and optimistic controls |

The exact counts are less important than the shape of the code. A current
textual coordination inventory makes the adoption result visible. Media
Sessions has no `lock` or `SemaphoreSlim` use and owns only its lifecycle
progress loop after adopting `WidgetModel<State>` and
`WidgetOptimisticCommand`. DLV-009 gives YT Music SDK-owned Active lanes for
auto-connect, progress, polling, and Latest transport reconciliation plus one
immutable presentation record and zero Task/CTS registry fields. It still owns
two narrow semaphores, one state lock, and a manual optimistic confirmation
policy in the same controller/view class. Spotify
has adopted paged resources, one immutable presentation projection, an explicit
selection generation, and named route/playback/presentation boundaries, but its
partial owner still contains four task fields, two semaphores, several lock
domains, and separate command, authorization, refresh, and polling paths. Audio Mixer and
Network Controls remain roughly 2,500 and 2,000 lines with about 61 and 44
textual lock sites and no adoption of the new model/resource/operation layer.
These figures are navigation aids rather than code-quality scores: they show
that safe coordination is still concentrated in the primary class.

Spotify's presentation also includes about 500 lines of GBSS and its dedicated
test program is about 1,200 lines. Those tests are valuable, but their size
reinforces that this is an application-scale reference. DLV-008 makes one route,
provider action, or view state findable without reading the entire widget; it
does not yet make the remaining lifecycle/authorization/refresh coordination a
copyable public composition.

The more important Spotify problem is not its exact 2,015-line count. `Render`
copies its manually locked authorization/playback/navigation/page state, then
reads `_playlists.Snapshot` and `_playlistItems.Snapshot` from independent SDK
owners. `OpenPlaylist` commits the selected playlist under the widget lock and
only afterward resets and starts the detail resource; the resource loader closes
over that mutable selection, while its snapshot has no playlist key. A render
can therefore combine a new playlist heading/route with detail state from the
previous selection. The resource epoch eventually rejects late work, but it
does not make the composed screen atomic. This is exactly the sort of subtle
coordination seam an advanced reference should eliminate before authors copy
it.

The existing Spotify tests cover paging, cache reuse, Back/focus restoration,
retry, sparse pages, and a cancellation-ignoring detail completion after Back.
They do not force rendering between selection commit and resource reset, and
cannot assert a selected-playlist/detail-source key because the public paged
resource is unkeyed. The next migration should first establish one immutable
presentation snapshot and a Spotify-local keyed detail owner; it should not
start with file splitting or a speculative public generic.

The SDK already provides important low-level safety mechanisms:

- [`Widget`](../src/WidgetSdk/Widget.cs) supplies runtime-owned widget, state,
  and active lifetimes.
- [`WidgetTicker`](../src/WidgetSdk/WidgetTicker.cs) supplies bounded,
  non-overlapping active updates.
- [`WidgetControllerQueue`](../src/WidgetSdk/WidgetControllerQueue.cs) supplies a
  bounded serial action queue with slider coalescing.
- [`WidgetOperations`](../src/WidgetSdk/WidgetOperations.cs) supplies bounded
  SingleFlight, Latest, and Serial lanes tied to Active, State, or Widget
  lifetimes, with explicit admission and non-faulting completion results.
  Games & Apps uses an Active Latest lane for initial/retry library reads,
  replacing author-owned cancellation-source lifecycle cleanup.
- [`WidgetPagedResource<TItem>`](../src/WidgetSdk/WidgetPagedResource.cs)
  supplies an offset-based immutable page snapshot, Latest coordination,
  bounded LRU, safe error/retry state, invalidation, and responsive Scroll-to-
  focus mappings.
- [`WidgetResource<TValue>`](../src/WidgetSdk/WidgetResource.cs) supplies a
  non-paged immutable loading snapshot, bounded freshness caching, duplicate
  coalescing, safe errors/retry, last-good retention, subscription publication,
  stale-result rejection, reset, and lifecycle cancellation.
- [`WidgetModel<TState>`](../src/WidgetSdk/WidgetModel.cs) supplies atomic
  immutable snapshots, serialized updates, equality-based invalidation, exact
  revisions, result-bearing mutations, and contained diagnostic observation.
- [`WidgetNavigator<TRoute>`](../src/WidgetSdk/WidgetNavigator.cs) supplies a
  bounded route stack, stable route input scopes, exact nested Back handling,
  return-focus memory, and route-owned cancellation.
- [`WidgetIds`](../src/WidgetSdk/WidgetIds.cs) supplies validated hierarchical
  scopes and deterministic opaque `KeyedId` leaves for durable domain keys.
- [`UI.NavigationShell`](../src/WidgetSdk/NavigationShell.cs) supplies one
  validated two-to-eight destination model, compact tabs, an expanded rail,
  optional expanded-only persistent content, and explicit controller focus
  edges around one shared page subtree.
- [`ScrollElement.Paginate`](../src/WidgetSdk/Elements.cs) supplies protocol-v11
  host-owned near-start and near-end focus triggers without visible paging
  buttons.
- [`WidgetTesting`](../src/WidgetSdk/WidgetTesting.cs) supplies typed fake
  capabilities and lifecycle control.
- [`ModernComponents`](../src/WidgetSdk/ModernComponents.cs),
  [`TileComponents`](../src/WidgetSdk/TileComponents.cs), and
  [`Toast`](../src/WidgetSdk/Toast.cs) cover settings rows, bounded pickers and
  action sheets, scrubbers, status/empty states, media/app tiles, and
  non-focus-stealing feedback.
- Protocol-v8/v9 responsive Grid and visibility branches, plus GBSS Row wrap,
  per-edge borders, `:pressed`, and bounded presentation translation, cover the
  low-level responsive/presentation contracts already required by advanced
  widgets.

Action dispatch now uses the supported short path. In commit `6c5f932`, direct
and controller actions share one bounded active-lifetime FIFO;
the worker and bridge acknowledge typed admission before provider completion,
coalesce compatible slider tails, and publish later failures asynchronously.
Bridge tests now wait for invalidation or failure rather than assuming an
acknowledgement means completion. That is the right framework ownership model
and removes the reason a normal author should invent detached work merely to
avoid the two-second request timeout.

It is now a coherent source-level author contract, but not yet verified end to
end. The capacity test uses an explicit action-start signal,
protocol-v1 empty acknowledgements and the legacy failure reason are preserved,
and public docs explicitly define catalog QuickAction as a non-authorizing
compatibility ingress. DLV-009 now binds YT Music's auto-connect, progress,
polling, and transport reconciliation to SDK-owned Active lanes; its remaining
connection/client semaphores serialize domain access rather than detached
lifecycle tasks. Ordinary refresh and transport commands rely on the shared
queue. Spotify has begun the intended migration by
deleting its command
task registry, action semaphore, start helper, and command-drain plumbing and
awaiting ordinary commands directly. First-Party Conformance exercises both
community packages through the generic isolated worker path, while their
typed-fake suites prove credential-free provider behavior; the widget-specific
tests still call `OnActionAsync` directly for provider commands rather than
enter through the production worker/bridge queue.

Commits `7d33ce1` and `7d92dcd` substantially improve the host-side failure path.
They add a strict bounded action-failure parser/queue, retain widget runtime
generation plus action/source IDs, reject control-bearing messages and false
restartability, drop stale generations, and bind generic copy to the affected
widget in both dashboard and open-widget footers. Cross-process failure text is
the stable generic `Action failed.`; provider-aware widgets render their own
safe domain errors.

The committed baseline through `7d92dcd` was lossy. Commits `7733a73` and
`707f850` replace it with a 256-widget/runtime-generation store, transparent
allocation-free lookup, a deterministic visibility/deadline controller, a
dedicated one-shot timer, and an idempotent controller-timer fallback.

Commit `ddb66c2` now supplies the missing production composition seam.
`WidgetActionFeedbackHost` owns the bounded catalog projection and controller;
`OverlayApp` injects the clock, Win32 scheduling, and invalidation callbacks,
publishes each complete bridge drain, selects exact dashboard/open identities,
and routes catalog replacement, Hide, Stop, and both expiry paths through the
same type. Its deterministic test feeds two widgets through one adapter and
proves offscreen isolation, one invalidation/schedule per batch, one-shot
expiry, fallback, stale-generation/catalog retirement, and no resurrection.
Dirty schema-v2 full run `20260809T232451Z-15d2e7b6` retains the 305-check
target, OverlayHost link, 41/41 steps, and 784 JUnit cases for `ddb66c2` plus the
then-dirty runner patch now committed as `7c8a5b8`. It is correctly
release-ineligible; clean full-gate
evidence still stops at `0598e5a`.

For authors, the intended contract is now coherent: await ordinary provider
commands and let the platform acknowledge admission and present a later generic
failure. Do not call the full product route proven yet. The adapter test does
not originate a failing installed Spotify/YT Music action through the real
worker/bridge/native controller or inspect painted/UIA status. The older global
transient status path also still expires only during drawing.

Commit `5804eaa` closes one important Spotify evidence gap. The normal
first-party conformance deployment now includes Spotify as both bundled and
installed content, configures a simulated connected backend, renders real
playback through the generic AppContainer worker, sends `spotify.next` through
`WidgetProcessClient.SendActionAsync`, waits for the broker call, and verifies
the exact Next operation. This proves the packaged worker/runtime/broker command
route rather than only direct typed-fake `OnActionAsync`. It does not yet prove
bridge/native controller ingress, asynchronous failure presentation, or a live
Spotify account.

Accessibility is also incomplete end to end, although the provider and shell
are now substantial. Authors supply accessible names and slider values; the
host projects exact final geometry, closed patterns/events, safe provider
lifetime, coherent slider values, quiet help/status, direct tray selection, and
one composite widget/footer/tray tree. Explicit origin, collision-proof
ownership, and nested-scope Back are now committed. Their remaining author-
facing gaps are exact route equivalence, inner-layer origin enforcement,
typed choice semantics, and packaged assistive-technology evidence. This is
host/platform work, not a reason to add author-owned Windows automation code or
duplicate host navigation.

Implementation status records focused Release results including
Widget SDK 84/84, Gbar CLI 49/49, SDK Gallery 6/6, YT Music 51/51, Focus
Navigation 41 checks, and Declarative Renderer 4,632 checks. Static inspection
confirms the relevant managed programs register those case totals, and focused
YT source coverage includes cancellation-ignoring stale ordinary and
authorization failures plus lifecycle exit. Clean all-lane result
`20260809T141527Z-8946c731` passed all 41 manifest steps and 755 JUnit-adapted
cases with zero failures, errors, or skips in 313.016 seconds for exact clean
commit `dc6b092`. Its retained bundle includes per-step logs/JUnit, 29 package
digests, exact selected native-toolchain provenance, zero stderr, and no stream
truncation.
The newer clean release-eligible result `20260809T152831Z-67b77c73` retains 182
passing JUnit cases across seven explicitly selected steps for docs-only commit
`6bd60d3`, including Runtime 44/44, Worker Host 9/9, CLI 49/49, Catalog 32/32,
Bridge 42/42, First-Party Conformance 5/5, and Documentation 1/1. Its `lane: all`
metadata does not make it a complete all-manifest run because the recorded
`selectedStepIds` narrow its scope.
Disabled and busy destinations intentionally remain focusable while activation
is suppressed; renderer and focus-test source encode that contract. The
repository's 33 managed test projects are all custom executable harnesses with
no standard test SDK. Current HEAD's internal runner now supplies a
41-step inventory, aggregate/per-step deadlines, capped output/case extraction,
JUnit and JSON adapters, and clean-only release eligibility. That is a strong
repository quality foundation, but it is not a versioned external-author test
product. The clean local result is release-evidence eligible, but there is still
no referenced immutable hosted workflow run. The available default packages
are one source version behind for YT Music and SDK
Gallery, so they are not current packaged evidence. Real controller, companion,
physical-display and performance-trace evidence remain pending. This review did
inspect representative retained PNGs, but the bundle is a dirty older widget-
body-only artifact rather than current packaged/full-shell visual proof.

These primitives prevent several classes of misuse, but the author still has to
compose them into an application architecture. Spotify is evidence that the
remaining gap is mostly reusable coordination, application composition, and
proof rather than basic rendering capability.

## Reassessment after Spotify 0.2.10 and the live paging failure

The latest Spotify changes improve both the widget and the framework:

- playlists and playlist items use bounded 12-item page windows rather than
  continually appending every loaded item to one snapshot;
- the worker keeps at most six cached pages for each collection;
- controller focus approaching a Scroll boundary emits a host-owned near-start
  or near-end action through protocol v11;
- the resource computes an appropriate entering-edge focus for previous and
  next pages;
- helper/widget tests cover pagination metadata, bounded snapshot size, cached
  reverse navigation, and requested focus, but bypass the full native host; and
- playback-host bootstrap reports SDK download failure explicitly and serves
  its trusted page from a stable internal HTTPS origin.

The protocol addition is a meaningful authoring improvement. A widget can now
request adjacent data naturally as the user navigates, without adding a visible
`Load more` button or detecting focus geometry itself.

Spotify no longer owns page-operation tasks, cancellation sources, generation
counters, page dictionaries, eviction loops, loading/error state, retry intent,
or compact/wide focus-mode parsing. Two `WidgetPagedResource<TItem>` instances
provide those generic behaviors with 12-row windows and a six-page/72-item LRU;
Spotify supplies provider loading, safe error copy, viewport IDs, item rendering,
and selected-playlist domain state. Queue remains a separate non-paged path.

The resource extraction is a successful example of moving bounded cache and
request behavior into the SDK. Commit `5c3ce72` now checks the original focused
Scroll for pagination before geometric focus can escape to a tab/rail control,
and a changed replacement-page `InitialFocusId` outranks stale exact/ordinal
memory. This directly addresses both source seams consistent with the user's
12/12/final-five-row reverse failure: earlier playlist rows disappeared as the
window advanced, scrolling back did not restore them, and only the final five
playlists remained accessible. Commit `023ea46` now drives real 29-item
wide/compact playlist and detail resources through 12/12/5 transitions, joined
slow input, cached reverse pages, exact IDs/focus requests, and provider counts;
native cases use matching Spotify scroll/rail IDs and prove one-shot focus
consumption. This is strong author-contract evidence, but the widget helper
manually selects and invokes the Scroll action while native tests use separate
trees. It still does not prove controller input through one real Spotify
snapshot, host, bridge, cache, replacement snapshot, and focus memory. Treat the
fix as Verifying until that composed test and the user's live retest pass. The committed shared
action queue now enables a second concrete simplification: Spotify deletes its
command task registry, action semaphore, start helper, and command lifecycle
drain, and directly awaits ordinary provider commands. The roughly 1,914-line
class still combines authorization task ownership, refresh serialization,
polling/progress loops, state/cache ownership, action routing, and view
composition, so this is useful migration evidence rather than a finished
structural decomposition. Existing Spotify tests invoke `OnActionAsync`
directly; add worker/bridge-path conformance before treating the framework
adoption as production-proven.
Immutable state now also has a medium production migration, including
optimistic transport commands. The next target should be one complete advanced
reference architecture, followed by a second migration that proves it is not
service-specific. Cursor and append/infinite-feed resource semantics still
need a separate design rather than being implied by the offset-paged API.

No authenticated Player, Queue, Playlists, or Devices screenshots were found in
the current evidence set; the stored Spotify images still cover configuration
and setup. The semantic and interaction tests are stronger, but credential-free
visual scenarios remain necessary to assess the complete playback UI.

The retained `final-schema-v2-20260808-final` bundle makes this gap measurable.
It records 44 dirty entries at revision `f3ac48c`, packages Spotify 0.1.6 rather
than current 0.2.10, and explicitly excludes shell/window, focus/input,
transitions, compositor, and physical-display fidelity. Its capture verifier
checks hashes, semantic invariants, computed-style presence, and renderer exit;
it does not judge clipping, overlap, focus visibility, scroll reachability, or
text truncation. The compact and 150%-text Spotify setup captures are legible,
but they do not answer whether a human can produce and maintain the current
advanced playback experience. Credential-free state fixtures and a clean
current package/profile matrix are therefore authoring infrastructure, not
optional release polish.

## Reassessment after navigation-shell and scenario-preview changes

The responsive navigation work closes one of the earlier high-priority
composition gaps. `UI.NavigationShell` accepts one selected destination, one
content entry focus target, one shared content subtree, and two to eight stable
destinations. It generates distinct compact-tab and expanded-rail control IDs,
publishes selected and disabled semantics, supplies wrapping directional focus
edges, and optionally inserts an expanded-only persistent pane. Built-in GBSS
owns the standard dimensions, focus, selected, pressed, and responsive styles.

Disabled and busy destinations intentionally remain in the focus ring. The host
separates navigation from activation so users can focus an unavailable control,
hear or inspect its label/state, and then receive no action dispatch. Native
renderer, controller-navigation, and widget-surface focus tests exercise that
contract. Keeping those destinations in the shell's explicit wrap ring and in
responsive persistence avoids dynamic focus ordering and resize-induced focus
teleportation.

Protocol v13 now carries `focusPersistenceId` only on focusable nodes. When a
resize hides the previously focused compact or expanded presentation, the host
restores focus to the single visible, same-scope control that explicitly shares
that key. Omitted, ambiguous, and cross-scope matches fail closed. Action IDs
remain routing intent and may legitimately be shared across different
destinations. `UI.NavigationShell` derives one bounded persistence key from each
logical destination ID and publishes it only on that destination's compact and
rail controls. This resolves the independent audit's identity concern in the
current code. Reconciliation now runs on resize, snapshot/presentation refresh,
and focus restoration rather than on every paint, so settled frames perform no
focus-persistence traversal. Implementation status reports focused managed and
native passes, but retained results and a direct test of the scheduling edges
are still pending. The native resolver intentionally classifies disabled and
busy candidates as focusable; responsive persistence should preserve that
stable logical destination while activation remains unavailable.

SDK Gallery has adopted the shell around its existing `WidgetNavigator`, so it
now provides a copyable example of one route model and one page subtree across
compact and expanded layouts. Focus and validation tests cover shared content,
distinct responsive IDs, selected-state accessibility, traversal, destination
bounds, and invalid declarations. This is a credible human-authorable path for
the main shell of a Spotify-style widget. Spotify itself has not yet migrated,
and persistent media panels, collection/detail pages, error pages, and action
footers still need comparable recipes.

YT Music independently demonstrates that the coordination helpers continue to
pay off outside Spotify. DLV-009 expands the earlier transport-only migration:
auto-connect, progress, polling, and transport reconciliation now use bounded
Active operation lanes, one immutable presentation record supplies rendering,
and cancellation-ignoring lifecycle races cannot commit. Companion confirmation
and rollback remain intentionally authored domain policy; provider/controller/
view composition remains the open human-authorability boundary.

The migration now applies the Latest currency contract to every attempt-local
outcome. Successful snapshots, retry status, and authorization failure all
perform their final `IsCurrent` check under the state lock shared with
replacement admission. Cancellation-ignoring fakes prove stale ordinary and
authorization failures cannot change status, pending optimism, connection, or
replacement execution; lifecycle-exit 401 is also rejected. Current connect,
poll, and reconciliation 401 behavior still returns the widget to pairing.

The new `gbar preview` command establishes only the data-only discovery portion
of the scenario recommendation. A bounded `gbar.scenarios.json` manifest names
a provider assembly and declared factory metadata. Listing validates strict
JSON, bounded paths, names, descriptions, and scenario count without resolving
or loading the assembly. Selecting any scenario fails with a fixed diagnostic
before assembly resolution; it does not invoke reflection, start a timeout task,
or write an output snapshot.

Semantic execution remains open because an in-process timeout cannot terminate
arbitrary managed code and would leave scenario code with the full filesystem,
network, process, and credential authority of the CLI. The next step is a
dedicated production-equivalent AppContainer/Job/IPC process boundary that can
be forcibly terminated, followed by deterministic state fixtures, the real
widget test host, and native capture without exposing secrets or live services.

Removing DLL execution is the right security decision, but it increases the
priority of that isolated scenario worker. The eventual command should replace
the removed convenience rather than reintroduce it under a trust flag: execute
inside the production-equivalent worker boundary, accept typed fake services,
produce a bounded validated snapshot, and support deterministic interaction and
capture. Canonical documentation snippets should generate fixtures through the
same public test/scenario API instead of embedding one-off serialization code
in every sample.

## Reassessment of standalone repository scaffolding

The generated source itself is readable and appropriately small: one `Widget`
subclass, stable semantic IDs, a three-button focus graph, typed shortcuts, a
bounded integer state, and direct invalidation. A human author can understand
that code without framework internals. The failure is the surrounding product
contract, not the C# example.

`gbar new widget` now resolves one explicit dependency contract. Under this
repository it discovers `WidgetSdk.csproj`; elsewhere `--sdk-project` must name
an existing non-reparse `WidgetSdk.csproj`. Resolution and MSBuild path escaping
happen before output creation, and no unpublished package fallback remains. A
focused test copies templates beneath an unrelated temporary root, proves the
missing-SDK case leaves no target directory, supplies the exact SDK project,
and successfully completes the generated project's Release build. It injects
`GBAR_TEMPLATE_ROOT` and points back to the checkout SDK, so it proves the
interim source override rather than a packaged CLI/feed or cloneable repository.

The generator's atomicity stops at that preflight. `NewCommand` then creates the
final target, recursively enumerates every file beside `template.json`, reads
each as text, performs global token replacement, and writes it immediately. It
never parses `templateVersion`, declares an allowed file inventory, distinguishes
text from binary assets, bounds the template tree, rejects reparse traversal, or
rolls back a later failure. An unreadable template or destination error can
leave a partial project; an unexpected backup file is copied silently; and a
future PNG starter asset cannot survive the text pipeline. Current tests do not
exercise any of those cases. A professional starter should be a strict versioned
input artifact and publish atomically, not be whatever files happen to exist in
a directory at enumeration time.

The original generated README was internally incomplete: it asked `gbar render`
to execute a DLL and then replayed the nonexistent output. Current HEAD
uses the correct `gbar dev` path and explains that render is data-only. It still
supplies a replay file but no typed-fake snapshot-export test or static
snapshot, leaving the advertised deterministic replay loop incomplete.
The public platform overview makes this gap more confusing by saying
`gbar new widget` scaffolds a “test/replay” workflow. The generated tree has
`replays/smoke.json`, but no test project, snapshot exporter, or snapshot input,
so a new author cannot execute that advertised workflow without designing the
missing test infrastructure first.
This does not require a new framework abstraction: `WidgetTestHost`,
`WidgetTestHostServicesBuilder`, and `SnapshotJson` are already public. The
starter should generate a small deterministic test executable that attaches
the widget, exercises at least one action/lifecycle transition, asserts the
state, and writes the exact `snapshot.json` named by the README. Authors can
then see and copy the supported credential-free state/testing pattern instead
of being told to design it themselves.

The source-to-package path has a second concrete break. The generated manifest
declares `payload/<WidgetName>.dll`, but the generated README's `dotnet build`
writes beneath `bin`; `gbar validate .` checks manifest and GBSS semantics, not
entrypoint existence. `gbar dev` succeeds because it privately builds a
temporary package generation at the declared payload path. It does not publish
that generation, and the template creates no release staging step. As a result,
the CLI README's later `gbar pack <generated-source>` command correctly fails
with `missing_entrypoint`. A new author gets a green build and validation before
discovering that development and distribution use different, undocumented
artifact flows.

That test should be a sibling project generated with the widget, or created by
an explicit `gbar test init` command. It should use a normal portable
`dotnet test` adapter or a published minimal Widget SDK test runner, include one fake-
provider example and one deterministic lifecycle/snapshot scenario, and run in
an ordinary external repository CI job. The repository's custom `PASS`-line
protocol is an internal compatibility concern; community authors should not
have to reproduce it or edit the platform's private verification manifest.

The framework should still treat CLI, template, SDK package, compatibility
contract, and generated README as one versioned deliverable. The current
explicit local-SDK path is truthful contributor tooling, not the production
distribution model. The production milestone is a clean-directory test that
uses only the released CLI/feed, builds the generated project, executes every
README command, validates and packages it, and leaves a standalone repository
that another developer can clone and build. The generated source is already a
credible human starter; publishing and testing the complete dependency/tooling
loop is what turns it into a platform authoring experience.

Make one bounded source-to-package operation own Release build, dependency
publication, style/asset staging, complete-package validation, and deterministic
packing using the same generation semantics as `gbar dev` without launching the
overlay. Keep raw directory packing as the low-level deterministic primitive.
The generated README and CI should use the higher-level operation, and a single
external-repository test should execute build, validate, generated snapshot
tests/replay, package, inspect, install-disabled, and isolated launch from the
result. This is tooling composition, not another widget SDK abstraction.

The same release should parse a strict template manifest listing each relative
file, text/binary mode, and packaged digest; reject unsupported versions,
unexpected paths, reparse traversal, and count/byte overruns; materialize the
complete project in a unique sibling staging directory; and rename it into a
nonexistent destination only after every replacement and validation succeeds.
Failure-injection tests should prove no requested target survives an unreadable
source, destination fault, unsupported version, or malformed/binary entry. This
does not add author-facing complexity—the successful command stays identical—
but it makes the CLI/template pair a dependable release unit.

Publication also needs an explicit API-governance boundary. `WidgetSdk.csproj`
currently contains only build-language settings and a source `ProjectReference`
to `WidgetProtocol`; it has no package metadata, documentation/symbol/source
settings, package validation, or checked-in public-API baseline. A textual
inventory finds about 201 public class/record/interface/enum/struct declaration
lines in `src/WidgetSdk`. That breadth reflects real platform capability, but a
new developer needs a small documented supported surface and predictable SemVer,
not every currently public declaration becoming an accidental forever-contract.
Before the first package, classify the intended author API, baseline it with
package/API compatibility validation, and require intentional host/API breaks to
update the compatibility range and migration notes.

## Reassessment of GitHub sharing and package trust

The current workflow is a credible and fairly complete manual developer-preview
distribution path, not a finished public trust ecosystem. An author can produce a deterministic
`.gbarwidget`, attach it to an exact GitHub Release, and give recipients an
exact SHA-256 pin. The CLI maps the shorthand to one named release asset,
applies bounded HTTPS/redirect/size/time rules, validates the locked bytes, and
installs a version-addressed package disabled. Updates cannot be published
while the widget is enabled, selecting a version remains a separate disabled-only action,
rollback is explicit, uninstall requires the widget to be disabled, and
replacement content observed by catalog validation receives a new digest-derived
runtime authority instead of inheriting consent or secrets. In practical terms,
package, share, install, update, roll back, and remove are supported manual
operations. The remaining ecosystem gaps are automated discovery and verified
publisher trust, not the absence of a GitHub lifecycle.

The workflow still asks too much of both authors and users. Authors have no
supported public SDK package/template feed or signing command. They must arrange
an independent authenticated channel for the digest and explain why a GitHub
release plus a matching hash does not prove authorship. Current HEAD now gives
Settings users an honest **Unsigned · publisher unverified** state,
the full sealed content digest, **Enable unsigned widget** copy, digest prefixes
for version selection, and digest-bound permission language. It still cannot
show the acquisition source, a verified signer, or a version capability delta;
the catalog does not retain a host-owned acquisition receipt.

The exact-byte contract is rechecked at meaningful boundaries: catalog
discovery recomputes the sealed content tree before enablement, and the bridge
does so again before publishing runtime authority. Commit `d171dc8` now carries
the catalog lease's handle-derived volume/file identities into runtime ACL
capture and rejects a changed object before journal publication or mutation.
That is the correct author contract: package authors continue declaring content
normally and do not manage ACLs, handles, file IDs, or recovery records. Current
HEAD addresses the
resource-bound half with a narrow `BoundedFileReader`: manifest
and integrity metadata use one restrictively shared maximum-plus-one read, and
tree hashing rejects early EOF or bytes beyond the encoded length. The
first commit reports focused Release catalog coverage at 27/27. The committed
manifest-pairing follow-up reports 28/28; this review did not execute it or
inspect retained output. The cases cover exact,
misreported, and changing-length seekable streams; non-seekable limit-plus-one
input; safe `int.MaxValue` sentinel arithmetic; exact-length hashing; and stable
error codes for static oversized manifest/metadata files. Verification now
parses an owned copy of the manifest bytes read from the same handle included
in the digest and returns the model with that digest; a direct case proves the
result remains stable after later path mutation. That committed slice did not
bind GBSS/imports or worker-loaded bytes. Current HEAD closes
the GBSS half with an exact relative-path/SHA-256 inventory and a bounded,
strict-UTF-8, digest-checking source provider; worker-loaded bytes remain open.

More importantly, verification still returns a mutable package path alongside
the paired digest and manifest. The
bridge derives unsigned runtime authority from that digest, while the worker
later loads the entry assembly and lazy dependencies by path. Installed GBSS
selection now comes from the verified inventory, and every entry/import is
read through one consumed-byte-bounded handle, decoded as strict UTF-8, and
matched to its exact per-file digest before parsing. Modified or late-added
style sources cannot compile under the package authority. Styling Release
coverage is implementation-reported at 23/23. The launch path now extends that
authority to every package file: each lazy start/restart reacquires the exact
digest and path/length/hash inventory, pins verified bytes, and applies direct
non-inheriting AppContainer grants for only that inventory. The lease remains
host-owned and follows the worker session. Public distribution still needs
adversarial managed/native/asset loader cases, an alternate-group-ACE policy,
and one enforced aggregate start deadline.

The implementation shows why this remains platform-owned rather than an author
convention. Catalog discovery still supplies the generic worker path plus
`--package-root`, `--widget-assembly`, and `--widget-type` strings, while an
internal `ContentLeaseFactory` carries exact verified authority separately from
`ProcessLeaseFactory` residency accounting. Authors receive neither raw
verification handles nor an API to recheck their own package.

The lease binds namespace as well as existing bytes. Verified files receive
direct read/execute grants, required directories receive direct traversal grants,
and neither grant inherits to a later name. Changed or inserted entries present
during admission fail before launch; an entry inserted later has no worker
authority. Each content digest receives a distinct AppContainer identity, and a
production-token test proves the new generation cannot read the prior granted
root. The remaining authority question is whether inherited or group-token ACEs
could bypass those direct grants on an unusually permissive catalog root.

The next author workflow should preserve the safe mechanics while reducing this
trust ceremony. `gbar pack`/future `gbar publish` should emit one canonical
release receipt containing the exact asset name, transferred digest, sealed
content digest, declared identity/capabilities, and—when implemented—a verified
publisher signature. Installation should persist bounded provenance outside
package-controlled content while omitting credentials, URL query data, and full
local paths. Settings should distinguish unsigned development,
verified signer, unknown signer, invalid signature, and revoked signer, and
show capability/authority changes before enabling an update. Until that exists,
documentation must keep calling GitHub sharing unsigned developer preview and
must not imply that AppContainer containment verifies the author.

Commit `b2d6f95` aggregate-bounds the multi-version workflow: defaults cap
IDs, versions per ID, total versions, installed entries, accounted bytes, and
elapsed discovery, while install prospectively refuses a new package that would
cross a count or byte quota. This is a meaningful ecosystem safeguard. The
`1c1f8bb` follow-up threads elapsed/cancellation checkpoints through recursive
tree enumeration and each at-most-64-KiB bounded hash read. A process-level
watchdog is still required for an already blocked Windows filesystem call.
Accepted versions are also still eagerly hashed and temporarily retain up to 512
complete file path/length/hash entries each during discovery; bridge runtime
closures retain only enabled active generations.

At the prior committed baseline the author/user recovery path was incomplete:
Settings collapsed any limit failure to an empty “catalog unavailable” surface,
while `gbar uninstall` began with the same full discovery that had failed. An
older catalog exceeding new defaults therefore required manual directory
knowledge. Commit `8a46d5f` now exposes the breached quota and a bounded,
path-safe cleanup projection through both Settings and `gbar repair`.

That failure has now occurred in the default local catalog. Spotify had 19
installed versions (0.1.0–0.1.7 and 0.2.0–0.2.10) against the default maximum of
eight, while YT Music had six. Settings rendered
`installed_widget_version_limit`; the active Spotify version remained 0.2.10,
but the normal uninstall/version-list path could not repair the tree because it
also requires successful discovery first. A community author or user cannot be
expected to know which AppData directories are safe to move. Catalog limits are
therefore not a complete authoring/distribution workflow until the host exposes
a bounded repair inventory that works specifically when normal discovery is
unavailable.

The committed design now has the right authority boundary: directory-name-only
bounded health inspection, exact-version retirement under the catalog operation
lock, `gbar repair`, and a confirmed Settings flow never open the candidate
manifest. Only the selected generation is protected; inactive history can be
retired while that selected version remains enabled. Catalog, Settings, and CLI
tests use enabled over-limit fixtures, protect the selected version, tolerate a
corrupt inactive manifest, recover normal discovery, and preserve enabled
selection. Direct current-state inspection finds Spotify still enabled and
selected at 0.2.10 with three versions instead of the former 19, so the reported
product failure is materially repaired.

Do not overstate the evidence. Newer full runs pass 41/41 steps and the latest
records 783 zero-failure cases, but both manifests are dirty and ineligible;
final commit `8a46d5f` has no clean retained gate. The 512-version recovery
projection measured 276.295 and 278.620 ms with 1,711,440 allocated bytes in
those bundles, while implementation status cites 326.382 ms without a retained
result found by this review. Keep full-discovery cost, sequential/restart/stale-
confirmation recovery, and a clean exact-HEAD run as the remaining workflow
evidence rather than reopening the implemented API design.

The recovery route is not yet easy to discover from the error that prompted it.
The quickstart, authoring guide, and CLI README teach the generic workflow, but
the exact quota codes do not appear in `troubleshooting.md` or
`diagnostics-and-recovery.md`. The compact Settings diagnostic shows
`installed_widget_version_limit` without telling the user to open **Installed
widgets -> Catalog recovery**. Add that mapping for every quota code, include
copyable `gbar repair` commands, and give controller users a recovery hint or
action from the compact failure state. Authors and users should never need to
search AppData or understand catalog internals to interpret this failure.

## Reassessment of residency defaults and ecosystem cost

The framework makes an individual worker's resource request explicit and
enforces it with a one-process Job. Current HEAD also reserves a
default application envelope of eight workers and 512 MiB of summed declared
Job limits before lazy launch. One exact trusted Settings worker is separately
bounded/accounted so a full application budget cannot hide the control plane.
This is a substantial improvement, but Settings still exposes neither
per-widget measured cost nor budget ownership/remediation, and existing
performance evidence covers one selected worker rather than ecosystem-scale
accumulation.

Current HEAD also repairs the ordinary authoring default. The
controller-widget template and Clock sample now choose five-minute
`unload-after-idle` and teach reconstruction from durable state. The protocol
retains `keep-alive` as its compatibility default, while authors must choose it
explicitly when continuous Background state is a real requirement. Conversely,
Spotify still needs keep-alive to preserve one temporary authorization
operation, demonstrating that residency remains too coarse a substitute for a
bounded critical-work lease.

The recurring-work audit confirms that residency and polling are not currently
conflated inside the advanced widgets. `WidgetTicker` accepts only 250 ms through
one hour, runs callbacks serially, and requires the active-lifetime token.
YT Music uses that helper for progress and provider polling and awaits both loops
on deactivation. Spotify's manual adaptive poll and progress ticker likewise use
the active lifetime and are awaited on deactivation; its widget-lifetime task is
limited to the already-authorized browser flow that must survive the overlay
moving to Background. Thus Spotify's `keep-alive` policy wastes potential
resident memory, but source inspection does not show its ordinary polling loop
continuing in Background. Preserve that distinction in documentation and
measurements so authors are not encouraged to solve temporary continuation by
moving all work to widget lifetime.

The host-owned admission now has exact lifetime ownership: the runtime acquires
a lease immediately before process creation and releases it only when that
exact process exits or its pipe/process/Job session is detached. Direct runtime
tests now cover pre-launch denial plus exact release across crash/relaunch and
cooperative stop; current bridge tests cover normal crash, timeout, and idle
unload. Clean all-lane result `20260809T141527Z-8946c731` retains the relevant
36/36 runtime and 40/40 bridge cases, including named pre-launch refusal, exact
lease, suspend, and idle-unload cases. Remaining fault injection includes
connection/protocol/companion
failure, live-process pipe disconnect, disable/removal, catalog replacement,
and bridge shutdown. The remaining product path is to make the user contract
complete.

Today a capacity refusal is flattened to generic `request_failed` plus message,
and the native client drops the code. An open widget without a snapshot can
therefore remain on `Starting isolated ...`: its four-second failure text is
used by the tray hint, not the in-widget footer. Settings remains reachable and
shows aggregate application worker and declared-memory totals, but it lists
worker identities only for recorded failures; a pre-launch capacity denial is
correctly not recorded as one. Authors and users consequently cannot tell who
owns the budget or reach the relevant unload/disable action from the refusal.
This needs a typed, bounded admission result, a persistent controller-facing
error state, and Settings attribution of each reservation and its residency and
remediation eligibility.

Commit `d2e49a9`'s exact-content launch work makes this contract more urgent. ACL
application occurs outside the documented admission timeout, and the native
host then waits synchronously without a request deadline. A stalled start can
therefore freeze the overlay before it can display any of those typed states.
Bridge requests need async, cancelable, deadline-owned host coordination;
authors should not be asked to compensate with smaller packages, manual startup
screens, or widget-specific retry loops.

The native-host audit identifies why this product state has no durable owner.
`OverlayApp` directly combines bridge transport, descriptor/snapshot caches,
lifecycle tracking, catalog retry, input sequence authority, focus/render
cleanup, and transient failure copy across roughly 3,753 lines. Low-level
helpers are tested, but the session transition that composes them is not. This
should remain a host implementation concern—widget authors must not gain a new
session API to compensate. A tested `WidgetSessionCoordinator` above the bridge
client should own typed per-widget `Starting`, `Ready`, `CapacityDenied`,
`Unavailable`, and `ProtocolFailed` states and emit bounded presentation
effects. The Win32 layer can then render/retry/remediate those results without
making every failure another timer-bound footer string.

That coordinator also needs an explicit `StaleLastGood` policy. A failed
snapshot refresh currently leaves a prior snapshot cached while showing only a
four-second message, so controls can still look active even though the worker is
unavailable. Widget authors should continue owning domain states such as service
denial, offline data, and provider retry; they should not add host-startup,
capacity, protocol, or process-crash screens to every widget. The platform must
qualify or disable stale controls and provide one persistent controller-facing
retry/remediation path.

The hidden host still has one separate performance debt that authors cannot
fix: when GameInput device tracking is unavailable, the legacy Guide adapter
polls all four XInput slots on a 25 ms timer so Guide can reopen the overlay.
That belongs to the native host's compatibility policy and measurement gate,
not a new widget lifecycle knob. The platform should adapt empty/connected-slot
cadence and prove wakeup versus Guide-latency behavior on legacy hardware while
keeping widget-facing active-lifetime semantics unchanged.

`keep-alive` should surface its
continuing cost during package review. The runtime should offer a bounded,
lifecycle-visible lease for rare
temporary work that genuinely must survive Background, or move that work into
the responsible broker. A supervisor budget may reclaim only widgets that
explicitly opted into unloading; it should never silently reinterpret
`keep-alive`. When pinned workers exhaust the budget, the author/user needs a
clear refusal and remediation path rather than hidden overcommit.

This keeps a simple widget simple while ensuring that “works in isolation” is
not the only performance standard. Author documentation and conformance tests
should include restart-from-durable-state, idle unload/reopen, denial at the
aggregate boundary, and measured multi-widget cost alongside the current
single-worker manifest checks.

## Reconciliation of earlier framework-gap findings

The following items were rechecked against public SDK declarations, protocol
validation, native behavior, and current author documentation. This prevents
implemented facilities from remaining on the roadmap under an older name.

| Earlier concern | Current verified contract | Remaining boundary |
| --- | --- | --- |
| `:pressed` was parsed but not rendered | GBSS publishes a pressed computed map, and the native host applies it only to the physically held action target. | No framework gap remains; widgets should style the semantic state instead of simulating it. |
| Motion lacked subtree translation | `translate-x` / `translate-y` move the complete presented subtree, including clip, hit-test, focus, accessibility, and Scroll geometry, with bounded retargetable transitions. | Shell-level presentation choreography remains host product work, not a widget API. |
| Settings rows, pickers, action sheets, scrubbers, toasts, and media/app tiles were missing | These are public `UI.*` compositions with stable generated IDs, semantic classes, validation limits, and controller behavior. | Standard page recipes beyond the implemented navigation shell remain open. |
| Layout lacked per-edge borders and responsive wrap/Grid | GBSS supports independent edge colors/widths and Row wrapping; `UI.ResponsiveGrid` provides protocol-v8 row-major reflow. | Virtualized/sectioned collections remain open. |
| Compact and expanded layouts required ad hoc host checks | Protocol-v9 `.VisibleWhen(...)` / `UI.ResponsiveBranch(...)` excludes inactive subtrees from every semantic system, and `UI.NavigationShell` now authors compact tabs and an expanded rail from one destination set around one shared content subtree. Protocol-v13 `focusPersistenceId` maps only explicitly equivalent, same-scope presentations and remains separate from action routing. Disabled and busy destinations deliberately remain focusable while activation is suppressed. | Other adaptive page structures and widgets not yet migrated to the shell still require lower-level composition; the public guide should make the focusable-but-inert contract unmistakable. |
| Sliders always consumed Left/Right during navigation | Protocol-v10 `.RequireControllerActivation()` reserves A/B for a host-owned adjustment mode; outside that mode all directions remain navigation. | A Slider cannot combine activation-first mode with a separate A activation action. |
| Typography lacked semantic monospace | `UI.CodeText` supplies bounded, whitespace-preserving semantic code text. | Packaged fonts and browser-style fallback stacks remain unavailable. |

The two correctness concerns raised in the previous pass are now reconciled:
YT Music guards attempt-local failure commits, and disabled destinations are
correctly focusable-but-inert by platform contract. The highest-priority
remaining authoring work is application composition and proof: cursor/append
resource variants, page
recipes beyond the navigation shell, broader coordination-helper migrations,
an ID/action analyzer, interactive credential-free scenarios, and native visual
capture.
Documentation should keep those
separate from already shipped primitives so authors can use the safe short path
today.

## Two different extension problems

The platform should distinguish these personas explicitly.

### Widget author

A widget author consumes existing public capabilities such as Spotify, audio,
network, media, or app-library services. The framework should make UI, state,
navigation, and safe asynchronous coordination straightforward.

### Capability provider author

A provider author adds new trusted access to an operating-system or external
service boundary. This involves broker contracts, consent, secret ownership,
native or trusted implementations, and security review. It is necessarily more
difficult and should not be disguised as ordinary widget work.

A human can build a Spotify-style widget only because `HostServices.Spotify`
already exists. A community widget cannot currently invent an equivalent
privileged service from inside its capability-free worker. Documentation and
templates should make this boundary clear.

## Design principles for improvements

1. **Preserve the simple path.** A Clock-style widget should still need only
   `Render`, `OnActionAsync`, and `Invalidate`.
2. **Use progressive disclosure.** Advanced helpers should be optional
   compositions, not a mandatory new base-class hierarchy.
3. **Make the safe behavior the short behavior.** Runtime-owned cancellation,
   bounded concurrency, stale-result rejection, and task observation should
   require less code than hand-written alternatives.
4. **Keep policy explicit.** Helpers must expose whether an operation is serial,
   single-flight, replace-running, latest-wins, active-scoped, or widget-scoped.
5. **Avoid hidden work.** No helper should silently poll, retain a worker, or
   broaden capability authority.
6. **Remain renderer-neutral.** Convenience APIs should emit the existing
   protocol rather than introduce Spotify-specific native elements.
7. **Prefer testable state transitions.** Domain state should be separable from
   rendering and transport.

## Recommended framework additions

### 1. Runtime-owned operation scopes — implemented

The public coordinator is now available from the protected `Operations`
property:

```csharp
protected WidgetOperations Operations { get; }

Operations.RunSingleFlight("refresh",
    async context => await RefreshAsync(context.CancellationToken));
Operations.RunLatest("page",
    async context => await LoadPageAsync(route, context),
    WidgetOperationLifetime.Active);
Operations.RunSerial("command",
    async context => await ExecuteCommandAsync(context.CancellationToken),
    WidgetOperationLifetime.Widget);
```

The implemented coordinator:

- binds each lane to `ActiveCancellationToken`, `StateLifetimeToken`, or
  `WidgetLifetimeToken`;
- returns non-faulting completion results and raises each failure once;
- cancels and awaits ending work before lifecycle transition callbacks;
- implements bounded Serial, SingleFlight, and non-overlapping Latest policies;
- rejects new active-scoped work while inactive;
- exposes busy state by stable operation key; and
- supports deterministic idle waits and focused tests without sleeps.

Admission is explicit (`Completed`, `Started`, `Joined`, `Replaced`,
`Enqueued`, `RejectedInactive`, or `RejectedCapacity`) and completion is explicit
(`Succeeded`, `Canceled`, `Superseded`, `Failed`, or `Rejected`). Latest
contexts become non-current synchronously when replacement is admitted. Bounds
are 32 tracked keys, 64 total active/pending operations, and 16 pending Serial
operations per key. Active spans Visible/Interactive, State belongs to one
exact runtime state, and Widget spans creation through Destroying. `Completed`
means the request was satisfied synchronously without scheduling work.
Busy-edge changes auto-invalidate the widget and also publish `BusyChanged`.

Spotify's non-paged destination loading uses one Active Latest lane. Its two
12-row playlist collections now use separate Active paged resources built on
the same coordinator. Other widget command/auth/polling paths can migrate
separately when their required lifetime and ordering policy are explicit.

### 2. Observable immutable widget state — implemented

Create the optional state container once in the widget constructor rather than
hand-rolling locking, snapshot copies, equality checks, and invalidation:

```csharp
private readonly WidgetModel<State> _model;

public PlayerWidget()
{
    _model = CreateModel(State.Initial);
}

var state = _model.Value;
_model.Update(state => state with { Status = "Refreshing", IsBusy = true });
```

The implemented contract provides:

- immutable snapshot reads suitable for `Render`;
- serialized updates;
- invalidate only when the value changes;
- an atomic update that can return an operation input;
- no reflection or ambient global store;
- contained `Changed` observation for diagnostics and tests, without retaining
  production history; and
- compatibility with ordinary fields for simple widgets.

`Value` is intentionally not cloned: authors must use immutable records or
otherwise treat published values as immutable. Update delegates run under the
model lock and must remain quick and side-effect free. A committed transition
increments the model revision and invalidates exactly once after releasing the
lock; an equal replacement does neither. The result-bearing overload derives
its result from the same prior state revision, so controller commands do not
need to reread unrelated mutable fields. Destroyed widgets may finish cleanup
state changes but no longer invalidate. This reduces lock scope and accidental
`Invalidate` storms without forcing a Redux-style architecture.

Media Sessions is the first medium production proof. Its session collection,
selection, pending command, view/status state, snapshot revision, live-update
availability, and reload admission now share one `WidgetModel<State>`. A
focused regression verifies that selecting a different session publishes one
invalidation and that repeating the selected action publishes none. Lifecycle
generations, stale-result rejection, and command admission remain intact.

Media Sessions also validates the command coordinator described below. The
model still contains domain state, while the coordinator owns admission,
lifecycle-bound execution, current-attempt completion, and the exact callback
sequence. Domain-specific projection, provider-event merge, safe messages, and
rollback remain explicit callbacks rather than hidden framework policy.

### 3. Non-paged resource state — implemented

Use `CreateResource<TValue>` for one current provider value that does not have
page semantics:

```csharp
private readonly WidgetResource<PlayerSnapshot> _player;

public PlayerWidget()
{
    _player = CreateResource("player.snapshot", new()
    {
        Load = token => LoadPlayerSnapshotAsync(token),
        MapError = _ => new WidgetResourceError(
            "player_unavailable", "Playback could not be loaded."),
        CacheDuration = TimeSpan.FromSeconds(30),
    });
}
```

The immutable snapshot exposes `NotLoaded`, `Loading`, `Ready`, `Refreshing`,
and `Error`, plus the current value, safe error, and revision. `EnsureLoaded`
reuses a fresh successful value, `Refresh` and `Retry` force a read,
`Publish` accepts an authoritative subscription value, and `Reset` cancels and
clears the resource. Identical in-flight reads join one operation. Late reads
cannot overwrite a reset or newer subscription publication. The default
Active lifetime, five-minute freshness window, and last-good retention are
explicit options rather than hidden polling or retry policy.

The resource owns state invalidation and bounded coordination, but the widget
still chooses when to read, how to render each state, and how to merge provider
events. It never starts work from `Render`. Cursor pages and append/infinite
feeds remain separate open designs.

### 4. Bounded offset-paged resource state — replacement-window behavior is not product-complete

The public resource is constructed once through `CreatePagedResource<TItem>`:

```csharp
private readonly WidgetPagedResource<Item> _items;

public LibraryWidget()
{
    _items = CreatePagedResource<Item>("library.items", new()
    {
        PageSize = 12,
        MaximumCachedPages = 6,
        MaximumCachedItems = 72,
        LoadPage = LoadPageAsync,
        MapError = MapSafeError,
        Viewports =
        [
            new("library.items.scroll",
                (_, absoluteIndex) => $"library.item.{absoluteIndex}"),
        ],
    });
}
```

Its immutable snapshot represents `NotLoaded`, `Loading`, `Ready`,
`Refreshing`, `LoadingAdjacent`, and `Error`; last-good retention is optional
and enabled by default. It validates offset pages, maps safe bounded errors,
retries the exact failed intent, rejects stale/late completions, coalesces an
identical request, and exposes explicit `Completed` admission for cache hits and
no-op boundaries. Limits are 100 items per page, eight cached pages, 512 cached
items, and a protocol-v11 threshold of 1–8. Cache eviction is deterministic
LRU, and its lifetime defaults to Active.

Protocol-v11 `ScrollElement.Paginate` remains the host-owned trigger. The
resource's `Paginate(scroll)` publishes only currently valid boundary actions;
`TryHandlePagination` accepts only an exact action and configured Scroll ID.
Viewport delegates receive absolute collection indexes and produce stable
entering-edge focus IDs for compact/wide surfaces. No worker focus geometry or
visible Load-more row is required.

Commit `5c3ce72` repairs the two inspected host seams: `MoveWidgetFocus`
resolves pagination against the current node before explicit/geometric movement,
and a changed valid replacement-page `InitialFocusId` takes priority over stale
focus memory. Commit `023ea46` adds real Spotify compact/expanded 29-item
playlist/detail resources, exact 12/12/5 IDs and call counts, slow joined input,
cached reverse pages, matching native topology, and a consumed-focus unrelated-
refresh case. Authors should not add custom focus geometry or duplicate visible
paging buttons.

Do not yet describe this as a proven complete controller workflow. The Spotify
test helper performs visible-index movement itself and directly calls
`OnActionAsync` with the Scroll action; native tests use separately constructed
trees. No fixture carries one real serialized Spotify snapshot through native
direction resolution, bridge/worker admission, resource/cache replacement, and
presented focus memory. Detail reverse also stops on the middle page, and the
original live failure has not been retested. Compose those existing pieces
instead of adding another helper layer. The one-shot refresh case now supports
the changed-ID design; introduce a new protocol token only if a composed test
finds a real ambiguity.

The 2026-08-09 packaged product report confirms the composition gap is real:
crossing a Queue or Playlist page boundary visibly moves focus from the bottom
to the top (and vice versa), while reverse Playlist traversal can oscillate
between the fixed Play action and the first row. Replacement-window paging is
therefore unsuitable as the final infinite-list authoring model even if each
individual page and cache transition is correct. DLV-006 must provide a
continuous keyed collection/viewport-anchor contract; DLV-022 then migrates
Spotify and proves fixed-header boundaries, forward/reverse loading, and focus
continuity through the production host path.

The resource owns its state-change invalidation and runtime-owned Latest lane,
including synchronous cache-hit/reset changes that have no operation busy
edge. The widget still owns copy and visual composition. Cursor paging,
append/infinite feeds, and a `UI.ResourcePage` composition are not implemented
and remain later design work.

### 5. Command and optimistic-update helper — implemented

Media, audio, network, and settings widgets repeatedly implement pending state,
busy controls, optimistic projection, success reconciliation, and rollback. The
public helper is created once over a `WidgetModel<TState>`:

```csharp
_playback = CreateOptimisticCommand(
    "playback",
    _model,
    new WidgetOptimisticCommandOptions<State, PlaybackRequest, ProviderCommand, Playback>
    {
        Policy = WidgetCommandPolicy.Latest,
        Lifetime = WidgetOperationLifetime.Active,
        Apply = (state, request) => new(
            state.Project(request),
            ProviderCommand.From(state, request)),
        Execute = (command, token) => Provider.ControlAsync(command, token),
        Reconcile = (current, command, result) =>
            current.MergeProviderResult(command, result),
        Rollback = (current, baseline, command) =>
            current.RemoveProjection(baseline, command),
        MapError = MapSafeCommandError,
        Fail = (current, baseline, command, error) =>
            current.RemoveProjection(baseline, command).WithError(error),
    });

var handle = _playback.Run(request);
```

The implemented semantics are:

- SingleFlight joins an in-flight key without projecting a duplicate; Latest
  replaces stale work and retains the first baseline through a replacement
  chain; Serial projects each admitted request only when its bounded FIFO turn
  begins;
- `Apply` derives both optimistic state and exact provider input from one
  serialized model transition;
- `Reconcile`, `Rollback`, and optional `Fail` receive the current model, so
  their domain merge can preserve unrelated provider events received in flight;
- Active, State, or Widget lifetime ownership, cancellation, and draining reuse
  `WidgetOperations`, including current-attempt rejection of late results;
- inactive/capacity rejection and a joined SingleFlight request do not call
  `Apply` and cannot mutate the model;
- provider exceptions are mapped to a validated `WidgetCommandError` with a
  stable code and at most 256 visible characters; mapper failure falls back to
  the bounded generic error; and
- mutating provider calls are executed once. The SDK never retries them.

`ShouldExecute: false` lets an admitted projection publish domain-specific
unavailable state without calling the provider. The helper does not infer
provider-event identity, confirmation deadlines, slider coalescing semantics,
or a correct merge/rollback for the widget. Authors must implement those
callbacks over immutable state. It builds on `WidgetOperations`; it does not
create a second controller-input queue.

### 5a. One action-admission contract — substantially implemented; native feedback proof open

Commit `6c5f932` implements the core shape: direct and
controller-resolved actions enter one runtime-owned bounded active-lifetime FIFO
before the host is acknowledged. Admission is typed, compatible slider tails
are latest-wins, action failures arrive later, and capability gesture context for
controller-resolved dashboard work begins inside queued execution.

Commits `7d33ce1` and `7d92dcd` add asynchronous failure ownership with a
bounded generation-owned native queue, strict payload validation, generic
cross-process diagnostics, and affected-widget feedback in dashboard and open
surfaces. The legacy catalog QuickAction path remains explicitly
non-authorizing; exact capability authority belongs only to snapshot-correlated
controller input and begins when its matching action dequeues.

Commit `ddb66c2` composes the platform-owned presentation half behind one
bounded `WidgetActionFeedbackHost`: complete failure batches, catalog/runtime
identity, dashboard/open selection, expiry scheduling/fallback, invalidation,
Hide, and Stop now share one production seam with deterministic 305-check
coverage. Authors should not add their own global toast registry for ordinary
command exceptions.

The source harnesses register Runtime 48/48 and Bridge 46/46 cases for slow/hung
prompt admission, deterministic saturation, mixed-ingress order, slider-tail
replacement, deactivation drain, late failure, no restart, exact gesture-
authority timing, typed direct/legacy quick admission, failure/process
separation, and runtime-generation transport. Native parser source covers the
bounded bridge consumer. This review did not execute those cases. Clean full
bundle `20260809T201448Z-0249ae81` passes 41/41 steps and 778 JUnit cases for
exact clean commit `0598e5a`. It covers these action changes, packaged Spotify
conformance, the paging repair, and accessibility through the actual-destroy
real-client test; it predates `b3558f9`, `59aae1a`, `6162937`, `8a46d5f`, and
`9ec0374`.
The newer 41-step runs are dirty and cannot replace that provenance.

The native presentation architecture is now implemented; finish the real route
before calling it end to end. Preserve the bounded per-widget/runtime-generation
store, allocation-free lookup, host adapter, one-shot scheduling,
controller-timer fallback, two-widget isolation, generation/catalog retirement,
and Hide/Stop cases. Exercise YT Music and Spotify failures through the actual
worker/bridge/native controller path and inspect the selected painted/UIA status
and expiry; `5804eaa` already proves Spotify's packaged worker/runtime/broker
success route. Their activation auto-connect and browser OAuth coordination has
a legitimately different lifetime from ordinary actions.

Authors should await ordinary provider commands in `OnActionAsync`; they should
not recreate a task registry or semaphore just for responsive acknowledgement.
Future ingress types must reuse this contract and declare their authority model.

### 5b. End-to-end accessibility provider — provider preview implemented; ship contract open

Widget authors should continue supplying stable IDs, visible text, accessible
names, values, and explicit selected/disabled/busy state through the closed SDK
model. Commits through `59aae1a` now turn that input into real widget, tray, and
dashboard UI Automation fragments with exact screen bounds, closed patterns,
events, bounded generation-checked UI-thread dispatch, one composite open-widget
root, and an explicit automation-versus-physical origin. Authors no longer need
to invent a parallel native accessibility tree or duplicate root host controls.

The previous low-level lifecycle and presentation defects are materially fixed.
`ProviderHost::Detach` disconnects UIA, clears host authority, and increments a
binding generation so retained roots/fragments become unavailable. Window focus
and visibility come from UI-thread-published state, clearing the tree does not
announce focus on a custom root, and root geometry changes emit bounds events.
The renderer, projection key, accessibility tree, and event diff now consume one
presented slider-value revision, so an optimistic scrub or volume change no
longer exposes a stale RangeValue by design. `02cc40a` separates routine non-live
help from transient polite feedback, `943d67b` selects tray items directly by
stable ID, and `0598e5a` proves real client roots become unavailable after
actual window destruction. These are host guarantees; authors should not
imitate them with custom labels, fake values, or duplicate controls.

Commit `6162937` directionally fixes the two prior composition bugs. Widget,
HostShell, and Tray domains now participate in AutomationId,
runtime identity, lookup, events, and queued authority, so authors do not reserve
shell prefixes. A typed nested Back is published for an active scope-root
pressed-B shortcut and revalidated before automation-origin dispatch. The
provider tests prove two domains can reuse `host.open.back` safely.

Do not call that author contract complete yet. Commit `6a079b6` now mirrors
the inspected SDK rules for focusless scope-root disabled/busy state, focused
shortcut precedence, focused-owner disabled/busy suppression, ancestor
fallback, stale focus, and nested-scope exclusion. It passes current focus into
both publication and invocation revalidation, which is the right behavior.
It replaces the reviewed throwing vector path with allocation-free bounded
recursion, so the `noexcept` publication path is now truthful. However, it
still duplicates the managed algorithm and no real UIA client
traverses `OverlayApp` into and out of SDK Gallery Picker, ActionSheet, or
Navigator. Drive both resolvers from one conformance corpus or publish one
closed result; authors should not add fake duplicate Back buttons to compensate.

Existing node kinds should keep closed Windows mappings: buttons and
ActionSurfaces to Invoke, sliders to RangeValue, progress to read-only
RangeValue, and choices to SelectionItem or Toggle only when the SDK declares a
matching choice/toggle contract. The current generic `selected` flag is not a
complete typed choice model. Styling classes such as `page-heading` must not
become hidden semantic metadata.

Commit `59aae1a` gives provider actions an explicit
`AccessibilityAutomation` origin. The bridge and runtime reservation refuse to
pair that origin with dashboard capability authority, and the base SDK action
queue omits its gesture context. This is the right author contract: an override
may inspect origin but must not decide security policy.

Commit `9ec0374` resolves the inspected inner contradiction.
The worker validates origin and creates ambient dashboard gesture context only
for `PhysicalController`; the broker adapter attaches sequence/snapshot fields
only after exact activation returns true while the context remains active. A
hostile custom override synchronously invokes a capability on an automation-
origin event and records no context, activation, provider call, grant, or
revoke. A separate adapter case pre-grants valid authority, denies activation,
and observes no provider call, making it sensitive to leaked gesture metadata.
This is the correct author contract—custom overrides cannot mint authority.
Dirty broad run `20260809T220617Z-bc1dd7a3` records Runtime 49/49 and all
41 steps/783 cases green, but it is based on a dirty `8a46d5f` tree and is not
exact release evidence for committed `9ec0374`; this review did not execute it.

Public accessibility documentation is internally consistent and correctly calls
the surface a preview. Identity and nested Back are now committed, but not
covered by retained clean evidence. Document the new `widget:`, `host:`, and
`tray:` AutomationId compatibility change for external preview clients.

The acceptance fixture should retain the adversarial raw-ID provider case and
add real client traversal: dashboard -> SDK Gallery -> Picker/ActionSheet/
Navigator -> nested Back -> root Back/Close -> tray. Include focusless,
disabled/busy, stale scope/snapshot, worker failure, resize/DPI, quiet
announcements, optimistic sliders, and the hostile synchronous capability
override. Retain that new case on clean exact-commit evidence, deliberate OS
HWND reuse, AppContainer self-automation denial,
a clean gate, and packaged Narrator/MSAA evidence. Current clean retained
evidence passes 41/41 steps and 778 cases for `0598e5a`; it predates `b3558f9`,
`59aae1a`, `6162937`, `8a46d5f`, `9ec0374`, `6a079b6`, and `023ea46`.
Newer full evidence is dirty and release-ineligible; the latest passing run also
overlapped another full run in the same checkout.

### 6. Navigation and focus model — implemented

Advanced widgets need a small controller-native router:

```csharp
private readonly WidgetNavigator<Route> _navigation;

public PlayerWidget() => _navigation = CreateNavigator(
    "player.navigation", Route.Player);

_navigation.Navigate(Route.Playlists, sourceFocusId);
_navigation.Push(new Route.Playlist(id), sourceFocusId);
_navigation.Back(sourceFocusId);
```

The implemented navigator owns:

- a bounded route stack;
- selected root destination;
- source focus ID and return focus restoration;
- nested input-scope identity;
- Back action publication only when a nested route exists; and
- a route-lifetime cancellation token that is canceled before each route
  invalidation; and
- exact pressed-B handling only when the action carries the current route's
  active `InputScopeId`.

`Scope(root)` applies the current generated input scope and publishes B only
for nested routes. `Value.InitialFocusId` restores remembered route focus, and
`RouteCancellationToken` owns work that must end when the route changes. Depth
defaults to eight and is capped at 16; known routes default and cap at 32. A
capacity overflow is an explicit `RejectedCapacity` result. Every open-widget
action now carries the active scope ID, including A activation, focused
shortcuts, scope-root shortcuts, and Slider changes, so stale nested actions
fail closed instead of being inferred from element-name conventions.

SDK Gallery is the production-style public migration. Its tabs use
`Navigate`, Picker and ActionSheet use `Push`, nested B uses `TryHandleBack`,
and leaving a route cancels its token before the new snapshot is published.

The responsive composition is now implemented as `UI.NavigationShell`. It maps
one destination set to an expanded rail and compact tabs, keeps one content
subtree, optionally adds a persistent expanded pane, and publishes explicit
focus entry points. Protocol v13 preserves the logical destination across a
responsive switch through one explicit persistence key even though each
presentation has a distinct stable element ID and actions may be shared.
Low-level Row, Stack, visibility, and input-scope APIs remain
available as escape hatches. SDK Gallery proves the router/shell composition;
Spotify and other multi-page widgets still need migration evidence.

### 7. Stable ID scopes — implemented

Large widgets can use the public hierarchical ID builder instead of assembling
every string by hand:

```csharp
var ids = WidgetIds.Scope("spotify").Scope(mode).Scope("player");
UI.IconButton(..., ids.Id("play-toggle"), ...);
```

Generated composite suffixes should remain documented and stable. A build-time
analyzer or `gbar validate` rule should detect duplicate literal IDs, unstable
index-derived IDs where a durable domain key is available, invalid focus
targets, and action IDs with no known handler in declarative action maps.

`Scope` and `Id` validate each dot-free segment and the final protocol ID.
`KeyedId(name, durableKey)` hashes a bounded durable key into a deterministic
opaque suffix, so unsafe provider text and provider identity do not enter the
snapshot. This helper creates stable identifiers; it does not register action
handlers or prove that an ID is semantically stable. The analyzer remains open.

Spotify's paged-resource migration no longer needs compact/wide source-ID
parsing, and `UI.NavigationShell` gives each responsive presentation distinct
element IDs. Protocol v13 now keeps the three concerns separate: element IDs
identify nodes, action IDs identify intent, and focus-persistence IDs identify
explicit cross-presentation equivalence. A future analyzer should preserve and
validate this separation.

### 8. Layout recipes and semantic styling — partially implemented

The component library is useful, but advanced widgets still carry hundreds of
lines of GBSS. Continue adding reusable, theme-respecting recipes rather than
service-specific controls:

- adaptive navigation shell — implemented as `UI.NavigationShell` with built-in
  semantic GBSS and responsive focus recovery;
- persistent media panel;
- scrollable collection page;
- detail page with nested Back behavior;
- status/loading/error page;
- compact transport row; and
- responsive action footer.

Recipes should be SDK compositions that emit current protocol nodes and
semantic classes. They must remain themeable and must not add background work.
Widget-local GBSS should primarily express identity and small variations, not
rebuild standard focus, spacing, disabled, and responsive behavior.

This recommendation is above the current primitive/composite layer. Authors
already have `ResponsiveGrid`, responsive visibility branches, Row wrapping,
settings rows, Picker, ActionSheet, Scrubber, status/empty compositions,
media/app tiles, Toast, and now one reusable navigation shell. The remaining
recipes should compose those contracts; they should not introduce parallel
controls with different focus or styling semantics.

### 9. Scenario-based preview and visual testing — manifest listing implemented

The first public contract is a deliberately static scenario manifest:

```json
{
  "version": 1,
  "assembly": "bin/Release/net8.0/MyWidget.Scenarios.dll",
  "providerType": "Dev.Example.MyWidgetScenarios",
  "scenarios": [
    { "name": "playing", "factory": "Playing" },
    { "name": "empty", "factory": "Empty" }
  ]
}
```

`gbar preview .` validates and lists declarations without resolving or loading
the assembly. `gbar preview . --scenario playing` currently fails closed with a
fixed diagnostic, and `--output` writes nothing. Tests cover strict parsing,
bounded declarations, traversal rejection, listing without an assembly, and
fail-closed execution/output behavior.

This is useful discovery and validation infrastructure, but it does not yet
preview authenticated-looking, empty, denied, or error surfaces. Executing those
factories in the CLI process would grant ambient authority and a timeout could
not safely terminate them, so semantic execution must wait for isolation.

The repository-internal auth-free evidence harness proves that packaged
workers, simulated broker services, production style resolution, and an
offscreen native renderer can be composed safely enough to export selected
first-party snapshots. It is not yet an author workflow: its retained bundle is
from a dirty older revision, Spotify covers setup only, Settings failed to
start, and the verifier records rendering success without a layout or visual
acceptance verdict. Reuse those proven components behind the isolated public
scenario contract rather than creating a second preview architecture.

The remaining target is an interactive and visual layer over the same named
states:

```powershell
gbar preview . --scenario playing --viewport compact
gbar capture . --all-scenarios --viewport compact,standard,wide,accessible
gbar test . --controller-replay replays/smoke.json
```

That layer should instantiate the real widget with typed fake services, drive
lifecycle and actions, render through the native host, show clipping, and
isolate and forcibly terminate misbehaving scenario code. It should never require real
OAuth, hardware, or user secrets. Visual artifacts should be deterministic
enough for review, with pixel comparisons used cautiously and semantic
snapshots retained as the primary contract.

The newly expanded public guide and quickstart make both NavigationShell and
scenario preview much easier to discover, but their copyable examples are not
compiled by `Documentation.Tests`. The invalid quickstart glyph and stale
focus/action wording are corrected, and the guide now lists the additive
protocol-v1–v13 feature matrix. The repository has 70 C# fences, while the
documentation suite validates links and selected headings/phrases only.
Canonical end-to-end snippets should still be compile-tested against the same
SDK reference an external widget uses so documentation cannot remain green
while recommended code drifts.

### 10. Higher-level test harness

Keep `WidgetTestHostServicesBuilder`, but add fluent scenario and interaction
helpers over it:

```csharp
await WidgetScenario.For(new SpotifyWidget(), services)
    .ActivateAsync()
    .PressAsync("spotify.play-toggle")
    .ExpectBusyAsync("spotify.play-toggle")
    .CompleteCapabilityAsync(...)
    .ExpectTextAsync("Updated in Spotify")
    .DeactivateAndAssertNoWorkAsync();
```

The harness should provide deterministic operation completion, virtual time,
route navigation, focus assertions, lifecycle leak detection, and standard
capability states such as unavailable, denied, revoked, stale, and delayed.
The scenario API should emit results through a portable test adapter and also
integrate with the platform's bounded internal verification runner:
stable scenario/case IDs, per-case and process-tree timeouts, structured
results, captured diagnostics, and exact SDK/template provenance. The current
committed runner is a useful platform foundation: it introduces a stable
41-step manifest, managed/native local and Windows lanes, 4 MiB per-stream and
10,000-case defaults, truncation metadata, test-project inventory checks, clean-
only release eligibility, logs, JUnit, JSON provenance, and 30-day CI retention.
Its clean all-lane result `20260809T141527Z-8946c731` passes all 41 steps and 755
JUnit cases in 313.016 seconds for exact clean commit `dc6b092`, with release
eligibility and exact package/toolchain provenance. Current HEAD gates the
real command on an event signaled only after a kill-on-close Job and both pumps
are active, bounds combined pump completion, records selected compiler/SDK/tool
identity, and SHA-pins workflow actions. Community-artifact provenance now has
entry/file/byte/output and 30-second process ceilings under the shared remaining
deadline; root containment plus per-file/total-byte/entry quota fixtures are
implemented, and the shared-time helper proves reduced budgets plus typed fail-
before-launch exhaustion. Immutable hosted workflow execution remains absent. These are internal
gate issues, not a protocol every widget repository should inherit. The 33
custom executable test projects prove many useful contracts, but their `PASS`-
line adapter and `verification-steps.json` should remain repository compatibility
layers rather than something every generated widget copies.

### 11. Templates organized by complexity

One starter cannot teach every level. Provide repository templates such as:

- `basic`: local state and actions;
- `data`: one typed capability, loading/error/empty states, and subscription;
- `media`: playback projection, scrubber, optimistic commands, and progress;
- `multipage`: responsive navigation, cached pages, nested detail, and Back; and
- `companion`: exact-port companion JSON with explicit security constraints.

Templates should use the same production helpers as first-party widgets and
remain small enough to read end to end. Every template should also generate the
same minimal sibling test project and CI-ready command, varying only the fake
capabilities and scenarios needed by that template's complexity.

## Recommended Spotify structure after the helpers exist

Spotify should remain an advanced reference, but it should be separated by
responsibility. A reasonable target structure is:

```text
SpotifyWidget/
  SpotifyWidget.cs             lifecycle wiring and action dispatch
  SpotifyState.cs              immutable domain/view state
  SpotifyController.cs         capability calls and commands
  SpotifyRoutes.cs             navigation and route data
  Views/
    SpotifyRootView.cs
    PlayerView.cs
    QueueView.cs
    PlaylistsView.cs
    DevicesView.cs
    SetupView.cs
  styles/default.gbss
```

Splitting files alone does not remove complexity, but DLV-008 now makes the
remaining domain complexity reviewable and enforces the boundary in the focused
suite. Framework helpers should remove repeated coordination only after the YT
Music migration proves a second consumer; the accepted split already exposes
the actual Spotify behavior without publishing a speculative abstraction.

DLV-007 completed the prerequisite state boundary before the directory move.
`SpotifyPresentationState` is now the immutable render-facing projection, and
playlist detail carries `PlaylistSelectionKey(playlistId, generation)` through
an atomic selection/reset/load transition. This remains a Spotify-local
composition; a generic keyed-resource API should still wait until a second
production widget proves the same key, cache, cancellation, and retry semantics.

Use deterministic phase barriers to prove the boundary: pause between selection
commit and detail reset, complete a cancellation-ignoring old load after a new
selection, and exercise rapid Back/open plus compact/expanded focus. Every
rendered playlist detail should assert that its heading and item snapshot share
one selection key. Once that is true, `SpotifyController` and the pure view
files above become meaningful responsibility boundaries rather than cosmetic
extractions.

An ambitious but useful target is for the widget-specific C# layer to fall
below roughly 600–900 readable lines, excluding capability DTOs and generic SDK
helpers. The goal is not minimum line count. The goal is that most remaining
code describes Spotify behavior or presentation rather than task plumbing.

## Prioritized roadmap

### Phase 1: remove unsafe repetition

Implemented foundation: protocol-v11 focus-edge Scroll metadata, the public
`ScrollElement.Paginate` authoring API, runtime-owned operation scopes,
immutable widget models, bounded optimistic commands, non-paged resources, and
bounded offset-paged resources. Spotify playlists are the first paged-resource
migration; the native boundary/focus repair is implemented but needs composed
and live verification;
Media Sessions is the first medium model and command migration.

Next work:

1. Complete DLV-006's continuous keyed collection and viewport-anchor contract,
   then use DLV-022 to prove Spotify compact/expanded forward and reverse
   controller flows through the real renderer, host, bridge, cache, and snapshot
   refresh. Crossing a load boundary must retain the adjacent visual row rather
   than teleporting to a replacement window; reverse traversal must not
   oscillate between a fixed header action and the first list row.
2. Preserve the unified bounded action admission and new native feedback host;
   prove an installed Spotify/YT Music late failure through worker, bridge,
   native controller, selected painted/UIA status, expiry, and no restart.
   Keep pagination/value coalescing and gesture authority explicit rather than
   creating widget-owned command registries.
3. Accepted DLV-009 supplies cancellation-ignoring stale-success, stale-failure,
   and lifecycle-exit fixtures from YT Music. Generalize these into a reusable
   operation-migration test recipe without hiding domain confirmation policy.
4. Define credential/session generation semantics before any flow allows
   credentials to be replaced while requests are in flight.
5. Deliver cursor/append resource state in DLV-006 separately from current-value
   and offset-page semantics; do not extend the replacement-window contract
   until it impersonates a continuous collection.
6. Completed by DLV-007 (`ff706d2`): Spotify has one immutable render-facing
   model and explicitly keyed playlist-detail owner with deterministic
   selection/reset/load interleavings. Generalize keyed resources only after
   another widget demonstrates the same contract.
7. Add focused recipes for provider-event merge, confirmation deadlines, and
   absolute-value command coalescing without making them implicit.

This phase should deliver the largest reduction in semaphores, cancellation
sources, task fields, locks, generation checks, and manual invalidations.

### Phase 2: improve application composition

Completed foundation: `WidgetNavigator<TRoute>`, `WidgetIds`/`KeyedId`, exact
active-scope action propagation, protocol-v13 explicit focus persistence,
`UI.NavigationShell`, host focus recovery between responsive presentations,
and the SDK Gallery production-style migration.

1. Document and test the deliberate focusable-but-inert disabled/busy contract
   at the public `UI.NavigationShell` boundary, including wrap, selected
   destination, activation suppression, and responsive persistence.
2. Add a transition-scheduling seam test and measure the bounded persistence
   resolver so the contract is proven correct and dormant on stable frames.
3. Standard page recipes beyond the navigation shell.
4. Stable-ID/action analyzer rules.
5. Split and migrate advanced samples by responsibility, including adopting the
   shell in a genuinely complex multi-page widget.

### Phase 3: improve feedback and onboarding

Completed foundation: strict bounded scenario manifests, assembly-free listing,
and fail-closed scenario selection with contract and safety tests.

1. Publish a matching versioned SDK/template set with an intentional checked-in
   public-API/package-validation baseline, and prove `gbar new` through
   build, supported preview, replay, validation, and packaging from a clean
   directory outside this repository.
2. Move scenario assembly execution into a dedicated production-equivalent
   AppContainer/Job/IPC process boundary.
3. Real-widget scenarios with typed fake services, lifecycle, and actions.
4. Viewport-aware native preview, capture, and controller replay, with
   deterministic bounds/focus/scroll assertions and reviewed tolerant images
   from clean current packages.
5. Fluent deterministic test harness.
6. Compile-test the canonical copyable guide and quickstart examples against
   the public SDK; the corrected glyph and protocol-matrix regressions
   demonstrate why prose-only checks are insufficient.
7. Complexity-tiered templates.

### Phase 4: broaden the ecosystem carefully

1. Extract the host's widget-session coordinator and finish supervisor-owned
   aggregate residency with typed persistent failure/refusal states, per-widget
   resource visibility/remediation, optional reclaim only for unload-eligible
   workers, and bounded temporary critical-work leases.
2. Finish and prove the committed host-owned package content lease: retain exact
   manifest, GBSS/import, assembly, dependency, and asset bytes plus namespace
   authority for each worker session; retain stale-generation-root denial and
   reject replacement, alternate group grants, directory rename/reparse substitution, and late additions
   under the production AppContainer token; bind ACL targets to the objects
   authenticated by the lease; preserve the committed focused 1,024-directory
   accepted edge and atomic 1,025-directory refusal, then retain them cleanly;
   preserve the committed host-private write-ahead journal and `0ff403a`'s handle/
   object-identity enforcement, then isolate unrecoverable records to the
   affected authority where safe and prove crash/restart recovery, moved/deleted/
   offline targets, arbitrary AppContainer ACEs, unrelated-package behavior,
   durable quarantine, and a host-owned verified repair workflow;
   put revalidation, authority application, process creation, and hello under
   one enforced user-visible start budget without blocking unrelated bridge
   work; clean up persistent ACL grants on every failure/teardown path. Then add an
   honest unsigned acquisition receipt and publisher signing,
   rotation/revocation, signed update metadata, and verified/unverified package
   states before public community distribution.
3. Publish a language-neutral runtime and snapshot wire specification.
4. Add conformance fixtures and golden messages independent of C# types.
5. Evaluate a second worker runtime only after author demand and resource
   measurements justify it.
6. Define a separate reviewed provider-development path; do not grant ordinary
   widgets ambient network, token, or operating-system authority.

## Success metrics

The improvements should be evaluated against measurable author outcomes:

- A C# developer can scaffold and run a basic widget in 15 minutes from an
  unrelated empty directory using only released prerequisites, without a
  platform source checkout or manual project-file repair.
- A one-capability data widget requires no author-created `SemaphoreSlim`,
  `CancellationTokenSource`, or unobserved `Task` field.
- Latest-wins work cannot commit success, retry status, authorization state, or
  rollback after supersession unless an explicit external generation proves
  that result remains globally authoritative.
- An offset-paged collection requires no author-owned page dictionary,
  stale-generation counter, cache eviction loop, or compact/wide source-ID
  parsing. This is implemented and exercised by Spotify 0.2.10.
- An optimistic mutation requires no author-owned admission task, command
  cancellation source, or stale-attempt gate. This is implemented and exercised
  by Media Sessions; domain merge and rollback callbacks remain authored.
- A multipage widget uses one route model and destination set for compact and
  expanded layouts. SDK Gallery now demonstrates this; a larger production
  migration remains the next proof.
- An offset-paged collection's focused tests can traverse a partial final page
  and cached or reloaded previous pages using controller directions, but
  packaged Spotify evidence shows the replacement-window model still jumps at
  page boundaries and oscillates against fixed header controls. Product
  acceptance now requires the DLV-006 continuous keyed collection followed by
  DLV-022 production composition evidence.
- Disabled and busy navigation destinations remain reachable and retain stable
  responsive focus, while pointer/controller activation is consistently
  suppressed and the unavailable state is exposed clearly.
- Named authenticated-looking and unavailable scenarios can be declared and
  listed without loading code, but semantic execution remains unavailable until
  the isolated preview worker exists. Real-widget interaction and native visual
  capture also remain open.
- Every generated README command is exercised by the release gate; the
  scaffold restores/builds through the supported SDK artifact and never emits
  an unavailable placeholder dependency or a rejected execution mode.
- Canonical copyable documentation examples compile against the exact released
  SDK, and a public widget test/scenario run produces stable structured results
  with bounded execution rather than only process exit and console totals.
- `gbar validate` distinguishes missing, oversized, changing, invalidly encoded,
  and integrity-mismatched GBSS sources with bounded path-safe diagnostics
  instead of reporting every file-provider rejection as `missing_import`.
- A GitHub-hosted widget can be packaged and acquired by exact pinned bytes,
  and every host-side compiler and worker session consumes only content held by
  the matching verified digest and path inventory; replacement, stale
  previously granted paths, alternate AppContainer-group grants, and late-added
  package files cannot inherit that authority. Authors never manage those
  hashes, handles, ACLs, or launch leases, and every supported package size
  starts within one enforced and measured host budget while unrelated widgets
  and Settings remain responsive. Before public distribution,
  Settings clearly distinguishes unsigned from verified publishers, shows a
  bounded source/digest receipt and version capability changes, and rejects
  invalid or revoked signatures.
- Installed ID/version/file/byte quotas prevent normal package acquisition from
  making catalog cost unbounded, and an over-limit legacy or externally changed
  tree remains repairable from Settings/CLI without manual filesystem deletion.
- A long session has an enforced aggregate resident-process/memory envelope;
  ordinary scaffolded widgets unload after a documented idle bound, genuinely
  retained work uses an explicit bounded lease, and Settings attributes current
  resource ownership before asking the user to resolve capacity pressure.
  Capacity, startup, and protocol failures remain visible on the affected
  widget with controller-reachable retry or resource-management actions.
- Advanced samples contain substantially more domain/rendering code than
  lifecycle and concurrency plumbing.
- Every advanced screen is rendered from one coherent presentation revision;
  a resource whose meaning depends on a route or selection carries that key,
  and deterministic interleaving tests prove a prior key cannot appear or act
  under the current heading.
- Deactivation, stale responses, command rollback, focus restoration, and task
  cleanup receive standard reusable tests.
- The SDK preserves current message, update-rate, memory, and lifecycle bounds.
- The simple `Widget` API remains source-compatible and no more complicated.

Suggested baseline metrics for each migrated widget:

- widget-specific lines by category: domain, render, coordination, tests;
- number of author-owned tasks, cancellation sources, semaphores, and locks;
- number of explicit invalidation calls;
- number of duplicated compact/expanded nodes;
- time for a new developer to add one page and one capability-backed action;
- defects found in cancellation, stale-result, focus, and rollback testing; and
- preview coverage across compact, standard, wide, and accessible viewports.

## Risks to avoid

- **A giant framework base class.** Prefer small composable helpers.
- **Implicit polling or residency.** Every source of continuing work must remain
  explicit, lifecycle-bound, and accounted within the host-wide worker budget.
- **A service-specific UI framework.** Spotify should use general media,
  navigation, resource, and command patterns.
- **A custom test runner per generated widget.** Preserve the repository's
  useful deterministic fakes, but generate one portable test-project shape and
  standard `dotnet test` or published SDK-runner command. Keep the platform's
  `PASS` parser and private step manifest internal so authors do not have to
  invent orchestration, CI parsing, or platform-specific result lines.
- **Magic string replacement without validation.** ID helpers and action maps
  should improve diagnostics, not conceal routing.
- **Premature language expansion.** Another SDK would duplicate current
  authoring pain unless the coordination model is improved first.
- **Weakening isolation for convenience.** Easier external integration must not
  provide ambient network access, secrets, raw device IDs, or desktop authority.
- **Treating in-process preview as a sandbox.** Static scenario assemblies are
  trusted local developer code; a timeout reports a stuck factory but cannot
  safely terminate arbitrary managed work in the CLI process.
- **Optimizing only for line count.** Explicit policy is preferable to concise
  but surprising behavior.

## Final recommendation

Do not replace the C# SDK or declarative widget model. Treat Spotify, Network
Controls, and Audio Mixer as design probes that reveal the same missing
application-level layer. Continue building that layer from small,
lifecycle-aware, testable primitives: operations, non-paged and bounded
offset-paged resources, immutable state, optimistic command coordination,
bounded navigation, and stable-ID scopes are now implemented and exercised;
the first responsive navigation recipe, explicit responsive focus identity,
and bounded scenario-manifest listing are also implemented. Isolated semantic
scenario execution, cursor/append resources, additional page recipes,
an ID/action analyzer, interactive/native preview and capture, publication, and
broader migrations remain.

The immediate need is composition proof, not another broad abstraction. Media
Sessions and DLV-009 YT Music now prove lifecycle/state ownership reduction;
YT Music still needs provider/controller, authored command-policy, and pure-view
boundaries before it is a complete teachable reference. Require another advanced
widget to reproduce any proposed boundary before promoting it into templates.
For Spotify, coherence comes before extraction:
one immutable presentation state and an explicitly keyed playlist-detail
snapshot must replace the current cross-owner render assembly. Give Audio Mixer
and Network Controls their own
explicit command-confirmation and multi-provider-merge designs rather than
forcing their domain rules into a universal helper.

The protocol-v11 pagination change is a strong example to repeat: identify a
generic behavior proven by a demanding widget, move the security- and
input-sensitive portion into the host, expose a small declarative SDK surface,
and leave domain policy with the widget. Applying that same approach to cursor
resource variants, additional responsive recipes, broader migrations, and the
interactive/native half of scenario tooling would remove much more author
plumbing without weakening the platform boundary.

The framework will be ready for sophisticated human-authored community widgets
when advanced authors mostly describe domain behavior and UI, while the SDK
owns cancellation, bounded concurrency, invalidation, focus restoration, and
deterministic testing, and the recommended patterns make every success and
failure commit prove it is still current.
