# Implementation status

Status: integrated Phase 0 platform prototype, 2026-08-09

This repository contains working native and managed components. It is not yet
a production overlay, signed public-distribution trust boundary, end-user
installer, or marketplace.

DLV-221 replaces the production hand-written Flex and Responsive Grid geometry
solver with pinned Taffy 0.12.2 while retaining the original native overlay,
renderer, Widget SDK/protocol, process model, scrolling/clipping, DPI snapping,
motion, focus, controller routing, UI Automation, and single-HWND placement.
The repository-owned Rust static library is built for x64 MSVC with Rust 1.97.1
and `cargo --locked`; its checked-in versioned C ABI carries only bounded POD
styles/tree data, one synchronous intrinsic-measurement callback, error codes,
and bulk layout results. Panics are contained as errors, C++ and Rust assert the
same ABI record sizes, and no allocator, string, Rust type, input owner, or
widget identity crosses the seam. The superseded production solver and any
dual-runtime path are removed.

Focused Release evidence passes 5 Rust bridge tests, 250 linked layout checks,
4,839 renderer checks, 589 shared-component geometry checks, 107 controller-
navigation checks, 17 controller-owner checks, 49 focus checks, 24 surface-focus
checks, 2,086 slider checks, 485 accessibility checks across the tree,
projection, provider, host, and real-HWND/UIA layers, and the real single-HWND
eight-identity widget-switch cycle. That cycle committed all eight complete
surfaces with maximum draw/commit/coordinated-geometry times of 2.904/1.688/
1.930 ms and zero shell-motion commit time. Three bounded churn samples pass
with 0.772 ms maximum projection p95, 0% hidden/visible-idle CPU, and 1.35 MiB
maximum private working set. The isolated production hidden-state observation
records zero timer, Guide-fallback, paint, and D2D-frame work; the complete
three-process tree used 132.1 MiB working set. The Release host is 2,164,224
bytes and the intermediate Rust static archive is 4,184,950 bytes.

The older auth-free standalone exporter and visible performance fixture have
baseline gaps unrelated to layout: the exporter still assumes Spotify's
retired managed AppContainer entrypoint after its full-application cutover, and
the performance fixture cannot establish a visible lifecycle for any current
widget. DLV-221 does not weaken or mislabel those routes. Physical controller
and display validation remains a separate user-owned ship gate.

DLV-131 adds a launcher-only `LauncherExperienceCatalog` without changing global
`ThemeCatalog` semantics. Its strict schema accepts only the eight documented
host-owned launcher slots, bounded region/grid/stack/overlay/inset recipes,
closed host parameters, launcher-scoped GBSS, and sealed local static images.
Duplicate/unknown fields, slot reuse, missing critical slots, overlap, geometry
and focus/Back failures, remote or executable/archive content, unsafe paths,
reparse points, excessive files/bytes/dimensions, and cross-widget selectors
fail with stable path-specific diagnostics. Four code-owned recovery descriptors
cover hero rail, cover wall, carousel, and compact grid; the bottom-rail and
left-rail/glass-panel reference structures validate through the same recipe
rules. Focused Platform Settings Release evidence passes 17/17, including the
separate catalog, deterministic content digest, and invalid-selection recovery.

DLV-132 adds a focused native launcher recipe resolver and adapter without
changing `OverlayApp`, existing widget layout, or renderer ownership. The pure
resolver selects compact/standard/wide against the live logical work area,
keeps the eight slot types bounded, validates critical focus/Back extents, and
atomically falls back to the matching built-in hero rail, cover wall, carousel,
or compact grid. The adapter accepts only host-created slot snapshots and sends
them through the existing `DeclarativeRenderer`; its exact result continues to
own paint, pointer, controller-focus, and UIA geometry. Paint order and canonical
semantic order remain separate. The focused Release fixture passes 257 checks
covering the two reference structures, all four built-ins, compact/720p/1080p/
taskbar-reserved/monitor-offset/150%-scale profiles, invalid-recipe recovery,
long-title/missing-art rendering, exact actions/Back, and a real offscreen D2D/
focus/pointer/accessibility projection. The affected production host Release
also compiles without packaging.

DLV-101 removes the arbitrary private-worker 256 MiB and one-active-process
ceilings while preserving the security and transport boundary. Every Windows
worker is still created suspended, assigned before resume to one non-breakaway
Job, accounted as a complete process tree, and terminated through kill-on-close.
Optional `resourceRequest.memoryMb` is positive advisory diagnostics metadata;
host admission still bounds application worker sessions and retains all IPC,
snapshot, capability, package, and lifecycle limits. A credential-free installed
AppContainer fixture writes 128 KiB to its private local-data profile, starts a
package helper, renders a bounded first snapshot, proves both processes are Job
accounted, and proves Stop kills both. Runtime coverage also admits a worker
above the former 256 MiB boundary, while Bridge coverage rejects an oversized
presentation without affecting its admitted neighbor. Focused Release evidence
passes Runtime 74/74, Bridge 78/78, Widget SDK 87/87, generic worker host 10/10,
and Widget Catalog 35/35; the latter three are retained at
`artifacts/verification/20260812T004209Z-f969acb6`.

DLV-015 composes representative Settings, YT Music, and Spotify semantic trees
through the production native accessibility adapter, open-widget shell,
free-threaded provider, `WM_GETOBJECT`, and a real Windows UI Automation client
bound to a pumping Win32 HWND. Compact 100%, standard 100%, and standard 150%
profiles cover exact preorder, unique IDs, names, roles, help/range values,
physical bounds, enabled/disabled/busy/selected copy, loading-to-error
replacement, hidden interactive exclusion, Invoke, RangeValue, focus actions,
and focus restoration after unrelated widget generations. The new cohesive
`RealHostAccessibilityTests` target owns the named fixtures, host lifetime,
client inspection, and sanitized evidence; existing provider and adapter tests
remain the lower-layer authority. Release evidence passes 183 composed checks,
the five-test native accessibility group, a fresh OverlayHost build, and the
smallest packaged YT Music OverlayHost/UIA fixture. The retained manifest at
`artifacts/evidence/dlv015-final/manifest.json` contains only fixed public
semantic states and no screenshots. Physical Narrator/MSAA and packaged
AppContainer/UIA validation remain manual ship-gate evidence and are not
claimed complete.

DLV-049 locks the user-reported four-session Audio Mixer reverse-scroll state
to the corrected shared-geometry baseline from DLV-021. The emitted production
snapshot retains the exact `audio.input.volume.slider` Up edge to
`audio.master.volume.slider`; no host fallback, managed Audio Mixer change, or
public protocol change was needed. A test-only provider state now stages four
real Audio Mixer sessions. DLV-121 corrects that production-HWND/UIA fixture for
the accepted DirectComposition owner: raw HWND enlargement cannot make clipped
content or UIA descendants visible because committed content geometry remains
authoritative. The fixture now proves the authored `audio-mixer` scope and
Master/first/fourth-session endpoints, then uses exact Down navigation to reveal
all four sessions, reverses through the exact first session and Microphone, and
uses one Up to restore Master and offset zero. It also retains 4-to-12-to-4
session churn, full-motion settling, and reopen evidence with no
`value_clamped [audio.root]` normalization. Each current target has on-screen
UIA bounds; clipped, stale, and wrong-scope nodes remain excluded. The fixture
uses an isolated process profile and never contends with the accepted production
owner. Release verification passes FocusNavigation 49, DeclarativeRenderer
4,839, ScrollEvidenceProbe 35, AccessibilityTree 16, and HostAccessibility 34
checks, the single corrected AudioMixerScrollHost production fixture, and a
fresh OverlayHost build. No managed widget, renderer, compositor, protocol,
aggregate, or capture work was needed; physical controller evidence remains
user-owned.

DLV-026 restores complete bidirectional Audio Mixer controller traversal in the
native host. The production focus graph was valid; scaled native layout placed
each Slider edge no more than one raster pixel beyond its rounded card content
clip, so the host incorrectly classified later offscreen targets as impossible
to reveal even while the root Scroll retained range. The renderer now tolerates
only that bounded raster-edge overlap while still rejecting controls hidden by
a wrong-axis or fixed nested clip. A credential-free worker runs the real
first-party Audio Mixer with 12 sessions, and the production-HWND fixture walks
all 14 controls down and back to master output at preferred, constrained, and
150% surfaces. Exact host records prove explicit target, active scope,
revealability, presentation/navigation/UIA bounds, monotonic offsets, the true
zero leading boundary, stable unrelated-session refresh, deterministic removed-
focus fallback, and subsequent session addition. Screenshot acquisition is
excluded from this functional fixture: every record marks visual evidence as
user-validated, with no `PrintWindow`, desktop/screen fallback, or screenshot
failure capable of interrupting the traversal.
Fresh integrated Release visuals remain user-validated. The authenticated test-only
`ScrollEvidenceProbe` owns path enablement, target/direction state, bounded
serialization, UTF-8 conversion, and atomic replacement; `OverlayApp` retains
only handshake-gated argument admission and typed focus/render calls, while the
disabled normal path performs no probe write. D-pad and analog controller
events continue through the same native focus-move authority, and no managed
widget or public protocol changed.

DLV-021 consolidates shared text and component geometry without adding widget-
specific offsets. The new narrow `NativeTextLayout` owner creates the one
DirectWrite plan used by intrinsic measurement and paint: text transform, font,
weight, uniform line spacing, font-derived baseline, letter spacing, wrapping,
ellipsis, and positive vertical ink overhang can no longer diverge between
those phases.
Intrinsic extents round outward to the active native-pixel grid before layout,
so later raster snapping cannot turn a tight one-line control label into a
clipped second line. `DeclarativeLayout` remains the flex owner and now
remeasures a non-wrapping Row's cross size at its final distributed child
widths; a SectionHeader trailing action therefore cannot preserve an obsolete
one-line text-stack height. `DeclarativeRenderer` consumes those two owners for
Button, ActionSurface, SectionHeader, and ordinary text placement and retains
only a small test-only painted-line-count seam. A separate focused component
fixture covers Games & Apps, Spotify `LIBRARY` and `Check configuration`, Now
Playing, Settings, and SDK Gallery at compact, standard, wide/150%, and high-
contrast/reduced-transparency profiles, including stable selected/busy/disabled
Button geometry. Final focused Release checks pass NativeTextLayout 25,
DeclarativeLayout 250, DeclarativeRenderer 4,769, and shared component geometry
589 checks, with NativeStyle green and the production OverlayHost compiling.
The verified deterministic bundle at
`artifacts/evidence/dlv021/20260811T040500Z/manifest.json` retains 16 Games &
Apps/Spotify standalone widget-body renders from seven authoritative snapshots
across 30 exact files with zero renderer diagnostics. Its exporter records the
existing Settings worker-start gap; Now Playing, Settings, and SDK Gallery are
therefore claimed by the direct production-renderer component matrix rather
than by unavailable semantic PNGs.

Responsive Row wrapping, protocol-v7 ActionSurface, protocol-v8 ResponsiveGrid,
MediaTile/AppTile, Toast, semantic CodeText, independent per-edge borders, and
the first production uses of the public Picker and Scrubber are implemented in
source and focused tests. Games & Apps uses AppTile for its full-tile launch/
catalog targets and lifecycle-safe Toast feedback. Settings uses ResponsiveGrid
for root categories and CodeText for schema/worker diagnostics. The authoritative
full Release verification aggregate is green; hands-on packaged visual/
controller evidence remains pending until the relaunched overlay is exercised.

The current authoring-coordination milestone also implements public non-paged
`WidgetResource<TValue>`, bounded `WidgetNavigator<TRoute>`, validated
`WidgetIds`/opaque `KeyedId`, active input-scope propagation on every standard
open-widget action, protocol-v13 explicit focus persistence, and the responsive
`UI.NavigationShell`. SDK Gallery is the production-style navigation/ID
 migration. YT Music 0.2.7 maps typed/status-only failures to bounded copy and
 uses SDK-owned Active operation lanes for connection, progress, polling, and
 latest-wins transport reconciliation; it neither retains provider response
 bodies nor renders unknown exception text. DLV-030 preserves its single
 lifecycle/committed-state owner while extracting closed action routing,
 connection transitions, optimistic confirmation/rollback, progress
 reconciliation, and snapshot-only presentation as directly tested value
 seams. It adds no task registry, cancellation source, revision counter, lock,
 or mutable owner reference. Focused Release suites pass Widget SDK 84/84, SDK
 Gallery 6/6, YT Music 55/55,
and Gbar CLI 52/52. Packaged controller/companion evidence remains separate.

DLV-037 keeps `WidgetProcessClient` as the only host lifecycle, restart-budget,
failure-reporting, and public request owner while moving each generation's
transport/resources into one internal `WidgetProcessSession`. The client falls
from 1,027 to 964 lines and from 26 transport/lifecycle mutable fields to 12
lifecycle/policy fields plus one session reference and internal test hooks.
The 352-line session owns the pipe/channel writer, process/Job, cancellation,
reader/companion tasks, and
process/content leases behind one shared bounded terminal task and attachment/
publication gate. Separate 65-line pending-request and 103-line dashboard-
reservation owners contain the
only correlation table and gesture table respectively. Direct fixtures prove
exact/unknown response correlation and drain, expiry and operation matching,
shared exact-once terminal cleanup, and same-numbered request/gesture isolation
across replacement sessions. Manually controlled process-backed fixtures now
also stop a launch paused before process creation, replace sessions while
Invalidated/ActionFailed/process-exit publication and gesture activation are
paused before admission, hold an actual response before correlation, and
complete a cancellation-ignoring old companion grant after replacement. They
prove transfer-time lease/companion cleanup, zero late worker starts or host
failures, correlation/admission refusal for stale effects, and explicit late-
grant revocation. The existing process-backed suite continues to
cover lazy connect, exit/restart, Stop, Background unload, companion recreation,
cancellation-ignoring bounded companion cleanup, lease release, and dashboard
revocation without changing framing, broker authority, or sandbox policy.
The original bounded dirty-worktree Release run
`20260810T200924Z-3320d271` passed Runtime 69/69, generic worker host 9/9,
Bridge correlation/drain and installed-session coverage 52/52, and documentation
contracts across 52 Markdown files in 54.7 seconds. Final correction run
`20260810T204532Z-3fc406c4` passed the expanded Runtime 74/74, generic worker
host 9/9, Bridge 52/52, and 52-file documentation contract in 49.6 seconds.
These are scoped implementation runs, not canonical aggregates.

DLV-039 keeps `WidgetBridgeServer` as the only pipe-session, framing, request-
routing, reserved-Stop, reply, and serialized-write owner while moving catalog
and client generations into one internal `BridgeClientRegistry`. The corrected
server is 892 lines/40,160 bytes, down from 1,312 lines/59,775 bytes, and
replaces its catalog, revision, registration dictionary, catalog lock,
residency budget,
nested registration state, idle-unload scheduling, restart, reconciliation,
and disposal policy with one registry reference. The 1,261-line cohesive
registry owns configured descriptors, current-generation identity, one catalog
lock, the existing per-registration operation and idle-state locks, residency
leases/budget, cached snapshots, dashboard sequence authority, restart,
replacement/removal, and terminal disposal. A separate 199-line internal
`BridgeClientNotificationLane` is the registration-owned bounded event policy,
not a second lifecycle or write owner. A 61-line internal
`BridgeFrameWriteBoundary` owns reply and event writer admission, the fixed
in-flight deadline, and session abort after a partial-frame timeout through one
production adapter; it does not change framing bytes or the public protocol.
Per-registration idle work is now retained in an owned task set, canceled at
replacement/terminal admission, boundedly drained to the existing deadline,
and explicitly observed if cancellation is ignored. A shared terminal
completion makes concurrent registry disposal wait
for the same exact cleanup boundary. There are no request-dispatcher, public
protocol/framing-format, capability, authority, sandbox, or native-host contract
changes. The limited internal pipe-session behavior change withdraws a canceled
reply or event that is still waiting for the writer; once its frame starts, the
unchanged four-second deadline either finishes it or terminates the session
before a later frame can follow a potentially partial stream. Direct value-
based fixtures exercise compatible descriptor refresh, replacement/removal,
stale generation events, idle-unload cancellation and drain, restart lifecycle
restoration, residency refusal/release, failed start, concurrent operation, and
terminal disposal without constructing the server transport. Focused Release
run `20260810T212025Z-194406ce` completed in 47.4 seconds and passed Widget
Runtime 74/74 plus WidgetBridge 57/57; it is scoped dirty-worktree evidence, not
a canonical aggregate. After the final restart-retirement task-observation
correction, focused run `20260810T212257Z-08e1d8b0` completed in 16.9 seconds
and passed WidgetBridge 57/57; the unchanged Runtime result was not repeated.
The residual registry remains one cohesive worker-generation store and
transition owner rather than a new application coordinator: it contains the
closed typed operations for every bridge request plus the nested registration
resource state, while pipe/session, dispatcher, diagnostics projection, and
serialized writes remain outside it. The correction keeps a retiring
registration as the reserved slot, carries exact-generation publication leases
through actual server sends, prepares and restores restart clients before
publication, disposes failed unpublished clients, and drains every captured
retirement before one shared bounded terminal outcome. Each generation now
owns one notification pump with one latest-invalidation slot and a 32-entry
FIFO for non-coalescible action/runtime failures. Only `Accepted` items acquire
a generation publication lease; `Coalesced`, `RejectedFull`, and
`RejectedClosed` items acquire none. Retirement cancels the lane and releases
all accepted items before client disposal. Catalog/current-generation mutation
only reserves retirement under the registry gate; external cancellation and
client disposal begin after that gate is released. Cancelled or deadline-bound
restart transfers its reserved old generation into the same tracked exact-once
retirement path. `_activeRetirements` plus each registration's resource and
terminal completion outcomes are the sole retirement-task owners; the former
stored-but-unconsumed registration task property has been removed. Event or
request cancellation is admitted only while waiting for the
serialized writer; once a frame starts, a bounded session deadline either
finishes it or ends the pipe before any subsequent frame. Terminal failures are
represented by a saturating count plus the first failure rather than an
unbounded exception list. Manually
controlled fixtures force restart/concurrent-request, lifecycle-restore
failure, old event/result replacement, admission coalesce/full/close, a
stalled 200-event burst, cancelled restart, outside-gate disposal, fatal
disposal, and multi-client terminal interleavings without sleeps. A direct
event-write-boundary fixture uses the production frame channel to prove that
queued cancellation writes no bytes and a body-stalled partial frame ends the
session at the fixed deadline without admitting the queued successor frame.
Correction run `20260810T215121Z-611ae0ac` completed in 47.2 seconds and
passed Widget Runtime 74/74 plus WidgetBridge 60/60; it is scoped dirty-
worktree evidence, not a canonical aggregate.
Final bounded correction run `20260810T223739Z-dc97cf71` completed in 44.859
seconds and passed Widget Runtime 74/74 plus WidgetBridge 64/64. It is stable
scoped dirty-worktree evidence, not a canonical aggregate or release-eligible
run.
The first event-boundary verification
`20260810T225953Z-4b1e10bd` passed the new deterministic boundary case but
finished 64/65 after the existing pipelined-request harness observed an invalid
frame prefix during teardown. The unchanged bounded repeat
`20260810T230321Z-920a6f9b` completed in 16.009 seconds and passed WidgetBridge
65/65, including both that pipelined case and the boundary case. Both artifacts
are retained as focused dirty-worktree provenance; Runtime was intentionally
not repeated.

DLV-045 names the retained misalignment owner and closes an independent reply-
write exposure. The pipelined harness waited for two invalidations even though
the per-generation lane intentionally coalesces queued invalidations. When the
second event did not exist, `WaitAsync` timed out around a frame read created
with `CancellationToken.None`; that abandoned read remained active, consumed
the next Stop reply's header, and made the successor read interpret the first
JSON-body bytes as decimal length `1919951483`. A manually sequenced stream
reproduces that exact value. One test-only terminal frame reader now passes its
deadline into the actual read, aborts and drains that exact connection on
timeout, rejects overlapping/successor reads, and prevents teardown Stop on a
terminal stream. The pipelined fixture waits for the one authoritative
coalesced invalidation before confirming both ordered actions in the snapshot.

Separately, a manually blocked raw frame proves that ordinary reply cancellation
after its header could previously leave four header bytes and release the
serialized writer. Replies and events now share `BridgeFrameWriteBoundary`:
caller cancellation applies only before writer admission, while an admitted
frame completes under the fixed deadline or aborts the session before another
frame. The retained DLV-039 event case still proves queued withdrawal and
partial-frame timeout; the new reply case proves exact completion before its
successor. Focused dirty-worktree runs `20260810T233739Z-bf5296da` and the
required unchanged repeat `20260810T233812Z-6b00735c` completed in 21.797 and
17.997 seconds respectively and both passed WidgetBridge 66/66. Earlier
DLV-045 runs `20260810T232737Z-88d73425` and
`20260810T232829Z-91036f97` are retained failure provenance that exposed the
boundary's propagated cancellation contract and the underlying coalesced-event
timeout rather than being treated as retry closure. No Runtime, native,
documentation, or aggregate suite ran.

DLV-040 moves diagnostics and authority-recovery policy out of
`WidgetBridgeServer` without moving authenticated routing, companion identity,
framing, serialized writes, or host-effect publication. The server falls from
892 to 616 physical lines and retains one read-only diagnostics source, one
snapshot projector, and one exact-token recovery projector. The source captures
immutable catalog/registry/residency/appearance/provider values and performs the
consent read; it owns no mutation, lock, task, or revision. The snapshot owner
alone increments the diagnostic revision and independently reduces malformed or
unavailable areas to bounded safe state. The recovery owner alone maps the
existing Runtime service's pending records and typed retry results, validates
32/64-character uppercase tokens before mutation, preserves the existing
commit-versus-cancellation gate, and observes canceled synchronous work. It
never accepts a path, SID, descriptor, object identity, raw clear, or replacement
authority. Direct value fixtures cover degraded and unavailable areas,
malformed recovery state, stale catalog mapping, the 64-item bound, exact-token
retry, failed verification, refusal, cancellation, and commit-winning recovery.
No public diagnostics schema, private transport, catalog/client ownership,
recovery authority, Settings behavior, or threat model changes.
Focused dirty-worktree Release run `20260811T031616Z-fc4a7976` completed in
33.812 seconds and passed WidgetBridge 68/68, Settings 53/53, and documentation
validation across 52 Markdown files. The run retained one stable dirty-status
fingerprint at accepted control-plane commit `c61536a`; no Runtime, native,
security aggregate, or unrelated suite was run.
After the final atomic catalog-diagnostics capture edit, focused run
`20260811T031849Z-591cf5c6` completed in 17.523 seconds and passed WidgetBridge
68/68 against the final source state.

DLV-046 makes the checked-in ControllerWidget input and `gbar new widget`
transactional. Version-1 `template.json` now owns one closed inventory of at
most 64 canonical relative files, labels strict UTF-8 replacement templates
separately from byte-preserved assets, and applies 64-KiB manifest, 1-MiB
per-file, and 4-MiB aggregate input bounds. The loader rejects unknown or
duplicate fields, unsupported versions, traversal/backslash/rooted paths,
destination collisions, reparse points, missing or undeclared files and
directories, invalid replacement destinations, unreadable inputs, and size
overflow before publication. The scaffolder holds an immutable loaded input,
writes the matching local SDK package and declared files into a private sibling
directory, runs production manifest/GBSS validation there, and performs one
directory rename only after the result is complete. Any failure or cancellation
removes that owned staging tree; an existing author path is refused and never
deleted or overwritten. The direct failure matrix covers manifest shape and
version, binary preservation, every bound, unsafe inventory, unreadable and
invalid UTF-8 inputs, late validation rollback, destination faults,
cancellation, and reparse input. The existing external temporary-directory
journey still builds, executes its generated lifecycle fixture, validates,
renders, replays, produces identical packages, and exercises install/version/
rollback/remove without checkout paths. No public SDK/protocol, template
visuals, package trust, external feed, native, or runtime authority changed.
Focused dirty-worktree Release run `20260811T033333Z-abe40db8` completed in
44.002 seconds and passed GbarCli/scaffold 54/54 plus documentation validation
across 52 Markdown files. It retained one stable dirty-state fingerprint at
DLV-040 baseline `df304c3`; no aggregate, native, Runtime, or unrelated suite
ran.

DLV-047 establishes one pre-release WidgetSdk release unit without publishing
it externally. `eng/WidgetSdkRelease.props` is the canonical
`0.1.0-dev`/package-ID/template-version source imported by both WidgetSdk and
GbarCli; both assemblies carry matching metadata, the closed template manifest
must use that supported version, and the local package plus generated
`PackageReference` use one exact `0.1.0-dev.local.<16-hex>` version. The
deterministic `src/WidgetSdk/PublicApi.txt` baseline currently describes 2,400
ordinal public symbols with a 5,000-symbol/1-MiB check bound. The focused
compatibility runner classifies exact removed or changed symbols separately
from compatible additions, fails either unreviewed delta, self-tests all three
classifications, rejects checkout paths and nondeterministic generation, and
provides one explicit `--update` command. Contributor guidance distinguishes a
reviewed compatible addition from an intentional breaking pre-release reset;
no obsolete API is retained solely for legacy support. Package fixtures verify
matching assembly metadata, ID/versioned nuspec, exact generated dependency,
and repeated byte determinism. No public API was changed by the milestone, and
no runtime protocol, external feed/publication, signing, native, or host
authority changed.
Focused dirty-worktree run `20260811T034449Z-1daebd3a` retained the first
DLV-047 attempt: WidgetSdk built cleanly and passed 84/84, and the compatibility
release-unit check passed all 2,400 symbols, before GbarCli reached 54/55 due to
an unrelated timed-out broken-worker dev-session fixture. No product change was
made for that timeout. The bounded failed-step retry
`20260811T034611Z-5af856cf` completed in 51.780 seconds and passed GbarCli 55/55
plus documentation validation across 53 Markdown files. No aggregate, native,
external feed, Runtime, or unrelated suite ran.
After making the package ID itself flow from the same canonical release
contract into the nuspec and generated project, focused run
`20260811T034900Z-0e3b2a40` completed in 48.341 seconds and passed the final
2,400-symbol compatibility check plus GbarCli 55/55.

DLV-050 converts only the newly introduced WidgetSdk compatibility project to
discoverable `MSTest.Sdk` 4.3.2 tests. Twelve named cases cover the current
reviewed surface, deterministic bounded generation, additions, removals,
signature changes, release-unit metadata, missing/oversized/malformed/path-
bearing baselines, and read-only ordinary execution. Intentional baseline
mutation is no longer a test-process option: the narrow
`tools/WidgetSdkApiBaseline` command owns the explicit update, writes only the
requested baseline through a bounded atomic replacement, and is itself tested
against a temporary file. The verifier opts `dotnet test` into Microsoft
Testing Platform, retains named MSTest cases in JUnit output, and continues to
run all pre-existing executable scenario suites through their unchanged
`dotnet run` steps. No existing test project was migrated, and no public SDK,
runtime protocol, template, package, or host behavior changed.
Focused dirty-worktree Release run `20260811T041316Z-596d7304` completed in
61.527 seconds. The verifier self-test passed, WidgetSdk built with zero
warnings/errors and retained its unchanged executable 84/84 contract suite,
the new Microsoft Testing Platform project passed 12/12 named compatibility
cases with all names retained in JUnit, GbarCli retained its unchanged
executable 55/55 suite, and documentation validation covered 53 Markdown
files. This was the assigned mixed-runner proof, not an aggregate; no Runtime,
native, packaging, or unrelated suite ran.

DLV-194 proves the existing pre-release SDK/template release unit as a portable
local developer artifact. A focused fixture copies only the built `gbar`
distribution, removes the template override, and invokes that executable from
a fresh repository-shaped directory. The copied tool scaffolds an exact
content-versioned `GameBarAlternative.WidgetSdk` dependency, builds the basic
widget from its project-local cleared feed into a fresh temporary
`NUGET_PACKAGES` root, validates its capability-free
manifest, and creates a catalog-valid package. The SDK nupkg contains only
`WidgetSdk.dll` and `WidgetProtocol.dll`; CLI, broker, Runtime, catalog, Settings,
absolute checkout paths, and source-tree project references do not enter the
consumer or widget package. This remains a local offline artifact rather than a
NuGet.org publication, signing claim, or post-1.0 compatibility promise.
Focused Release evidence passes GbarCli 62/62, WidgetSdk compatibility 12/12,
the isolated external build/validate/package route, and documentation validation
over 66 Markdown files.

DLV-048 makes the marked quickstart blocks the one canonical offline author
journey instead of maintaining an uncompiled parallel starter in the larger
authoring guide. The exact generated `VolumeControl.cs` lifecycle/state/action/
focus source is embedded once and compared to the temporary scaffold before
that same file is compiled and executed. One cohesive GbarCli contract parses
the eight marked source/command fences, rejects missing/duplicate/advanced or
absolute-path content, and binds their new/build/test, validate, render,
replay, pack/install, version/select/rollback, and disable/remove claims to the
external fixture. The fixture asserts public success output, exported snapshot
semantics, lower/raise/apply replay actions, byte-identical checkout-path-free
packages, two immutable installed versions, selection/rollback, and complete
uninstall. An intentionally invalid GBSS file proves failure output names
`styles/default.gbss` and the validation action before the valid source is
restored. Advanced `gbar dev`, scenario-provider, custom-worker/capability,
AppContainer, credentialed service, and authority-recovery guidance remains
explicitly outside this credential-free path. No public API, runtime/native
authority, widget behavior, external publication, live service, or screenshot
work changed.
Focused dirty-worktree Release run `20260811T040047Z-d8e6fe07` completed in
39.912 seconds and passed GbarCli/scaffold 55/55, including the bounded external
author journey, plus documentation validation across 53 Markdown files. No
aggregate, native, Runtime, live-service, external-feed, or screenshot suite
ran.

DLV-022 migrates Spotify Queue, Playlists, and playlist detail from
replacement-page windows to the shared protocol-v14 cursor collection. Stable
opaque keys derive from exact playlist IDs or media URIs, never titles or
visible ordinals. Twelve-item provider responses append/prepend within a
24-item retained window; reverse traversal refetches evicted segments, refresh
retains an existing anchor or selects the shared deterministic fallback, and
empty/sparse/final/error states retain bounded last-good behavior. Identical
cursor intents now join the exact current SDK completion and call the provider
once, while differing cursor, direction, viewport, or refresh intents preserve
latest-wins replacement. Playlist Play and the first detail row author one
explicit reverse edge; the first row's forward edge continues into the list so
controller replay cannot oscillate with the header. Focused Release evidence
passes Widget SDK 85/85, Spotify 41/41, and first-party generic AppContainer
conformance 6/6. The standalone command validated the authored inputs and
packed Spotify 0.2.11 as a three-file 134,274-byte archive. No aggregate,
provider, OAuth, public API/baseline, protocol-format, native, live-account, or
screenshot suite ran.

DLV-053 corrects the rejected DLV-022 media-occurrence assumption without
weakening shared duplicate-key rejection. URI remains the semantic media key;
one private matcher uses immutable item evidence and neighboring collection
context to distinguish repeated queue and playlist occurrences, retains at
most the 24-row collection window, and uses deterministic nearest-equivalent
matching only for otherwise-identical rows. Action and focus IDs carry the
exact occurrence key. Playlist Play points Down to the first row and the row
points Up to Play, while an exactly one-row playlist now omits the former self
Down edge. Focused Release evidence passes all 45 Spotify cases, including
same-page and cross-page duplicates, eviction/refetch, refresh insertion and
deletion, exact action routing, bounded reset, and the complete prior DLV-022
collection/lifecycle matrix.

DLV-055 publishes the current immutable Spotify Community package as 0.2.12
without changing widget behavior or architecture. The supported Release
package/install workflow validated the authored inputs, packed the expected
three-file 146,050-byte archive, installed and explicitly selected 0.2.12,
enabled it, and retained 0.2.11 as an inactive rollback version. The archive
SHA-256 is
`99d52e89f5a922fa7347db0bf3a4665e64184925a319d958cdc5dd49f257fa4b`.
An independent install of that exact archive produced the same content-tree
digest as the selected installed generation
(`a946b36c8f818681de5ec39febfd00322accff14f435bfde274a6c94bd8aafef`),
while retained 0.2.11 remained distinct
(`abb2cd6f6340b69204489c39b60bb3cd18462010407f60c8f7c4b16ca70fa522`).
The smallest credential-free production generic-worker fixture loaded the
selected installed package under required AppContainer isolation and returned
a valid first Spotify snapshot at sequence 2. The 45-case Spotify behavior
suite, aggregate, provider, native, screenshot, and live-account verification
did not run for this packaging-only milestone.

DLV-043 replaces Spotify's four-declaration, 2,207-line logical partial widget
with real private responsibilities. The sole non-partial 1,275-line root keeps
all lifecycle, three cursor resources, provider calls, three task fields, two
locks plus one semaphore, committed state, and invalidation. It reaches a
137-line closed route/action classifier and a 140-line playback/device command
policy only through immutable values/results; neither policy owns a provider,
task, lock, resource, or invalidation. The 763-line presenter consumes only one
immutable `SpotifyPresentationState` and owns no mutable authority. The root's
34 existing state/resource/coordination fields and its singular lifecycle
ownership are not duplicated across the boundaries. Direct deterministic
fixtures cover repeated semantic presentation, route/action admission, and
playback command/projection rules, while the retained route/Back, provider-event
versus command, late result, cancellation, and Active-drain cases remain green.
Focused Release evidence passes Spotify 48/48, Widget SDK 85/85, and generic
AppContainer conformance 6/6. No aggregate, live account, provider, or native
suite ran.

DLV-056 makes managed Community-package payloads opt into one shared deterministic
build input. `eng/CommunityPackage.props` retains portable PDB generation and
maps each opted-in project directory to `/_/community/<project>` before the
compiler emits the PE CodeView record; the Spotify project imports that input
explicitly. The supported pack script also supplies one stable repository-root
map as a global publish-graph property, so the Widget SDK and protocol reference
assemblies have identical MVIDs and reference fingerprints in every checkout.
This removes the absolute checkout path previously embedded in
`SpotifyWidget.dll` without changing public debugging, SDK, protocol, provider,
or widget behavior. DLV-056's clean-root proof did not cover an ordinary warm
main checkout: its package command could reuse the default incremental
`bin`/`obj` graph and produce a different same-length managed payload after
integration.

DLV-057 makes the supported package command own the complete generated build
graph under its selected artifact root. Every run removes only that bounded
script-owned graph, then passes it to `dotnet publish` through
`--artifacts-path`; ordinary developer `bin`/`obj` outputs are neither consumed
nor deleted. Checkout path mapping and bounded NuGet cache reuse remain intact.
Spotify 0.2.14 is the resulting immutable Community package; the supported
workflow retains 0.2.11, 0.2.12, and 0.2.13 as inactive rollback generations.

DLV-208 publishes the already-accepted vertical-rail responsive source and GBSS
as new immutable Spotify Community package 0.2.15. The supported package path
produces a three-file 153,415-byte archive with SHA-256
`9f3735f454792a4289b7b37c31292043f4ebbb92b1f7e4c8ec0d6a9cb4d65960`.
An isolated update disables 0.2.14, installs/selects/enables 0.2.15, retains
0.2.14 as an inactive rollback, and independently reproduces selected content
digest `0762775563e6e339b035a2d683cca6cb4e8d327834ee7f63220f814a3970264f`.
The installed payload hash equals the script-owned publish output, installed
GBSS equals the accepted source, and the generic AppContainer worker returns a
valid first snapshot at sequence 2. Spotify's existing responsive, focus,
navigation, configuration, consent, private-state, and provider behavior is
unchanged; the focused widget suite passes 50/50.

DLV-051 corrects Spotify's authored responsive focus graph without changing
host navigation. The inactive seek Slider now names the stable currently
selected `spotify.nav.wide.*` rail destination as its explicit Left neighbor;
the compact Player names `spotify.nav.compact.player`. One deterministic
credential-free case inspects both emitted responsive branches, replays the
single Left input through the existing controller replay, covers all four wide
destinations plus a same-route playback refresh, and retains the prior seek
Down and Previous/Play/Next edges. The production package builds through the
ordinary public SDK path, with no provider/OAuth, public SDK/protocol, native,
playlist paging, or architecture-boundary change.
Focused stable dirty-worktree Release run `20260811T043026Z-ea419336`
completed in 12.499 seconds and passed Spotify 40/40 plus documentation
validation across 53 Markdown files. The separately bounded package command
validated two authored inputs and packed the expected three-file Spotify
0.2.10 archive at 127,106 bytes. No aggregate, provider, native, live-account,
playlist-matrix, or screenshot suite ran.

DLV-001 completes the bounded AppContainer authority-recovery operator surface.
The Runtime retains a profile-owned pending record until every original DACL is
restored and verified, then makes journal clear versus cancellation one atomic
decision. PlatformDiagnostics carries only bounded recovery IDs, safe display
copy, closed status codes, and exact opaque 32-character current or 64-character
legacy confirmation tokens over the private PID- and nonce-authenticated
Settings channel. Settings starts explicit controller confirmation on Cancel,
never renders the token, and refreshes authoritative state after every typed
result. The separate local `gbar authority-recovery list|retry` workflow has no
force-clear, caller-selected path, SID, ACL, or replacement-authority input.
Current focused Release suites pass Runtime 65/65, PlatformDiagnostics 15/15,
WidgetBridge 47/47, Settings 45/45, and Gbar CLI 52/52; the documentation
contract passes with the Settings and CLI operator guidance present.

DLV-002 makes Games & Apps reconcile the bounded trusted catalog on initial and
subsequent activation. Only entries classified `Game` by a reviewed provider
are added automatically; `Application` and `Unknown` remain explicit opt-ins.
The widget owns a schema-v3 private-state policy for ordered SavedIds,
automatic-membership provenance, exact SavedId exclusions, and selection.
During single-user development, v1, v2, unsupported, or invalid documents reset
as a whole to empty v3 state before fresh trusted-catalog reconciliation; no
legacy membership, exclusions, projection, selection, or launch authority is
partially retained. Missing identities keep
their bounded order tombstones, reappearing identities receive fresh launch
tokens without focus drift, and a different SavedId remains a distinct game
even when its display title is identical. A bounded CAS merge preserves a
concurrent exclusion, and count-only automatic-add notices never take focus.
Final focused Release verification retained at
`artifacts/verification/20260810T040923Z-1a5ccb27` passes the Windows provider
31/31, broker 49/49, Games & Apps 39/39, and the documentation contract across
51 Markdown files. The final widget cases include bounded page-two discovery,
authoritative reclassification, deterministic focus fallback, and cancellation
and drain of cached background reconciliation before Catalog paging. The
minimal package/AppContainer Tier-2 run retained at
`artifacts/verification/20260810T040949Z-b933b8f2` passes 6/6. DLV-002 does not
authorize or claim another repository-wide aggregate; packaged physical-
controller and visual evidence remains in the verification queue.

DLV-003 moves Button leading visuals, labels, and selected/busy/unavailable cues
onto one native content-placement model. Center alignment now centers the
complete icon-label group, balanced cue lanes keep state changes from moving it,
and explicit start/end alignment remains edge-stable with collision-free
trailing cue space. Intrinsic Button measurement consumes the same icon/gap/cue
width used by paint before DirectWrite computes wrapped height. Existing shared
NavigationShell and Spotify navigation recipes that author `justify` on a
Button leaf now inherit start/center/end content alignment without widget-local
offsets or protocol changes. The focused Release renderer target passes 4,685
checks across text-only/icon, selected, busy, disabled, focused, wrapped,
compact/standard/wide, 100-150% text, and 1.0-1.5 pixel-scale cases; the
production OverlayHost Release target builds successfully. Retained standalone
evidence at
`artifacts/evidence/dlv003/dlv003-shared-geometry-final/manifest.json`
verifies 12 Games & Apps/Spotify captures and 24 exact retained files with zero
renderer diagnostics. Its semantic exporter records the existing Settings
worker startup gap explicitly. Full-shell physical-controller, display, and
assistive-technology sign-off remains verification-only evidence.

DLV-005 established deterministic tray-Y tap/hold arbitration. DLV-209 restores
its intended production action after DLV-193 narrowed it incorrectly: release
before 700 ms retains reorder, while crossing the threshold restarts the exact
selected bundled or installed bridge widget through the same worker authority as
F5. No descriptor or widget-authored Refresh action is required. The host
revalidates tray focus, non-reorder state, and selected widget before dispatch;
accepted shell transition, focus loss, overlay hide, controller loss, or worker
lifecycle change cancels progress, and a winning or canceled gesture consumes
its stale release. The focused production fixture covers packaged Settings and
one installed Community worker, including exact-once threshold, repeat, and
release behavior. Physical-controller threshold proof remains in the
verification queue.

DLV-004 repairs the Games & Apps product surface without changing catalog
authority, protocol, or native geometry. Library, Add applications, loading,
healthy-empty, and failure states now share the same bounded responsive
hierarchy and the public SectionHeader, StatusBadge, Card, EmptyState, Alert,
AppTile, and Toast components retain their shared style classes. Catalog
navigation keeps only one 32-row page in a semantic snapshot while preserving
bounded opaque Next/Previous cursors through the current provider revision. Render
no longer publishes a mutable element-to-app lookup; actions revalidate the
current route and stable SavedId-derived element, then resolve that SavedId once
more before using the newly issued short-lived AppId. Back preserves a curated
selection, and removal chooses the
nearest surviving row. Focused Release coverage includes shared loading/empty/
error surfaces, a 64-entry long-name Library, maximum Catalog traversal,
bidirectional focus, mutation, lifecycle, and GBSS flex contracts. Retained
standalone captures cover automatic, Catalog, and mixed Library surfaces across
compact, standard, 150%-accessible, and wide/high-contrast profiles. Exact
focused integration evidence at
`artifacts/verification/20260810T053135Z-3f63fdf8` passes the 6/6 installed
generic-worker/AppContainer conformance group; final Tier-1 evidence at
`artifacts/verification/20260810T053438Z-64812600` passes Games & Apps 42/42
and the 52-file documentation contract. The clean-commit
capture bundle is published under
`artifacts/evidence/dlv004/dlv004-product-surface/manifest.json`; packaged
physical-controller/display evidence remains a separate release gate.

DLV-014 composes a real advanced-widget post-admission failure through the
production OverlayHost process. A credential-free deterministic worker
constructs the production YT Music widget with a complete connected snapshot,
then raises a non-lifecycle cancellation from its command client. The worker
runtime converts that failure to the sanitized action-failure contract; the
managed bridge binds `controllerActionFailed` to the widget, runtime generation,
action, and source; and the native host paints `YT Music action failed; try
again` while projecting the same bounded four-second polite UI Automation
status. The production-HWND fixture opens the widget and activates
`widget:play-pause` through the host keyboard mapping, observes
`LiveRegionChanged` without focus loss, proves replacement extends the deadline,
Hide prevents resurrection, Stop terminates the original worker, and verifies
that private exception text never enters `overlay.log`. This covers the real
host action ingress but not physical GameInput hardware, whose evidence remains
separate. The canonical native Release script passed, including 305 focused
feedback checks, 149 accessibility-provider checks, 32 host-accessibility checks,
and the production-process fixture; the managed bridge suite passed 47/47, and
the isolated YT Music Community-addon acceptance passed without changing the
real user catalog.

DLV-017 adds a bounded display-only warm-start projection to the Games & Apps
schema without persisting launch authority. A fresh worker immediately renders
the saved order, selection, automatic/explicit membership, and exclusions as
disabled **Checking…** AppTiles containing only SavedId, a sanitized 20-scalar
name prefix, and closed kind. Fresh resolution atomically replaces them with
short-lived AppIds while SavedId-derived focus and order remain stable. Missing
identities stay visible but disabled, reappearance receives new authority,
authoritative reclassification still hides automatic non-Games, and refresh
failure retains last-good display with explicit safe status. Active-lifetime
transitions and failed authority refreshes discard cached AppIds before showing
the projection, so only a current exact resolution enables launch. The exclusion cap
is 128 so the proven worst escaped-Unicode schema remains below 64 KiB. Focused
Release coverage passes 49/49, including atomic legacy/invalid-state reset,
current-v3 mutation and restart continuity, delayed fresh-worker resolution,
stale-launch refusal, CAS/exclusion behavior, failure retention, and a
cancellation-ignoring completion rejected after Background.

DLV-020 makes open-widget switching one host-owned presentation transaction.
The last admitted snapshot and surface remain painted as visual-only content
while a newly selected isolated worker has not produced its first snapshot;
controller and accessibility authority already belong to the destination, so
the retained controls cannot receive actions or enter the UI Automation tree.
This removes the transient `Starting isolated Spotify widget…` / Games & Apps
surface from ordinary switches without delaying snapshot admission. Once the
destination snapshot is valid, the existing host cadence reveals it and eases
the logical extent from the currently presented size over a bounded 140 ms;
reduced motion snaps immediately, and a reversal retargets from the presented
extent. `WM_SIZE` resizes the existing Direct2D HWND target in place and rebuilds
only dependent resources, while aliased color-key rounded fills prevent a dark
blended fringe. The production-HWND fixture delays Spotify for 240 ms and Games
& Apps for 320 ms, proves the prior content and exact extent remain intact for
that interval, then captures both size transitions, rapid reversal, and a
same-identity Games refresh with no blank shell, tray loss, or black-border
frame. Physical display and controller evidence remains separate.
The canonical Release script passed the 108,547-check placement suite, the
54-check targeting suite, the 56-check transition suite, the 25-check chrome
suite, the 4,685-check renderer suite, and both production-host fixtures. The
bounded DLV-020 run passed with 44 retained frames under
`artifacts/evidence/dlv020-final8/manifest.json`; representative startup frames
show Network continuously painted through Spotify admission and Spotify
continuously painted through Games & Apps admission, while the Games reload
retains the same 981x668 host extent and Games content. The documentation
contract passed across all 51 Markdown files.

DLV-024 repairs the managed Library mutation boundary exposed by the catalog
remove/Back sequence. Catalog and Library removal now compute an immutable
schema-v3 delta without changing the render projection, serialize it through
the existing bounded CAS path, and publish only the committed or conflict-
merged state. The removed SavedId can no longer become an invalid persisted
selection that normalizes the entire Library to empty. Write failure retains
the complete prior in-memory and durable Library, while conflict recovery keeps
unrelated membership, display projection, order, exclusions, and current-
lifetime resolved launch rows. A removed focus target selects the nearest
survivor through the committed SavedId selection. Initial/background/explicit
reconciliation retains the Ready Library and exactly one Add applications
action rather than substituting a transient loading tree. Library and Catalog
preferred height is 600 DIPs, more than two 78-DIP row pitches above the former
430-DIP Library baseline when host safe area permits; compact and 150% profiles
remain Scroll-bounded by their existing minimums.

The installed-worker regression had a second deterministic trigger: the
20-rune display projection could end on a space, then fail its own canonical
trim validation and normalize the complete desired v3 state to empty. Display
projection now trims that truncation boundary before validation. Broker-shaped
opaque-ID coverage reproduces the original `Conformance Trusted Game` label and
proves the SavedId, automatic provenance, selected row, and canonical display
copy survive the private-state write. Final focused Release evidence passes
Games & Apps 52/52, Widget SDK 84/84, Windows Community private state 10/10,
and the fresh isolated first-party generic-worker sequence 6/6.
Retained evidence at
`artifacts/evidence/dlv024/20260810T090000Z-dlv024-final-v2` verifies 7
authoritative semantic snapshots, 2 interaction traces, and 16 standalone
widget-body captures with zero renderer diagnostics. The before/removing/removed
Games sequence covers standard, compact, 150% accessible, and wide profiles;
the bundle honestly retains the unrelated Settings worker-start evidence gap.

DLV-027 separates the stabilized Games & Apps implementation by stable
responsibility without changing its public behavior or schema. The widget
remains the sole lifecycle/action/provider/launch/publication owner behind one
state lock and one command semaphore. `GamesAppsPresentation` is snapshot-only;
`GamesAppsCatalogPolicy` owns immutable bounded paging transitions; and
`GamesAppsLibraryPolicy` plus `GamesAppsLibraryStore` own schema-v3 mutation,
projection, exclusion, order, trusted-Game reconciliation, conflict merge, and
the bounded two-attempt private-state CAS transaction. Three widget-local lists
that duplicated committed membership, automatic provenance, and exclusions are
removed; `GamesAppsLibraryState` is the single committed policy value. Focused
tests invoke removal, provider reconciliation, CAS conflict merge, Catalog
forward/reverse transitions, and pure presentation without the complete widget,
while a source-boundary contract rejects provider/lock/persistence ownership in
presentation and duplicate lifecycle/render ownership in policy seams.
The bounded focused Release result at
`artifacts/verification/20260810T102345Z-7c1b0cdd/verification-result.json`
passes Games & Apps 55/55, generic worker lifecycle 9/9, Windows private state
10/10, fresh first-party installed-worker/AppContainer conformance 6/6, and 52
documentation contracts in 83.4 seconds. DLV-027 did not run the canonical
aggregate.
The bounded correction removes the unused render-time presentation counter and
proves repeated immutable presenter input produces byte-identical serialized
semantic snapshots after normalizing only the host sequence. Focused result
`artifacts/verification/20260810T103446Z-d7991b69/verification-result.json`
passes Games & Apps 56/56 and 52 documentation contracts; unchanged worker,
private-state, and conformance boundaries were not repeated.

## Implemented

### Native overlay and input

`src/OverlayHost` starts hidden, uses a GameInput Guide callback, stops ordinary
polling/rendering while hidden, and presents a Win32/Direct2D controller
dashboard. It supports reorder mode, last-widget/order persistence, focus
routing for the reference widget, deterministic tray-Y selected-worker restart
through the shared F5 recovery path, managed bridge startup, bounded HTTPS
artwork, fixed native semantic icon geometry, responsive logical viewports,
per-monitor placement, and snapshot-gated visual/extent transitions that retain
host chrome and the last admitted widget presentation across worker startup.

Asynchronous widget action failures are retained as fixed generic copy in a
host-owned, 256-widget-bounded controller keyed by widget ID and runtime generation.
Concurrent failures for different widgets no longer overwrite each other;
dashboard and open-widget footers resolve only the requested widget's current
generation. Catalog replacement/removal and overlay hide discard retained
state. The controller accepts caller-supplied monotonic time and returns the next
deadline plus whether visible state changed. A dedicated timer removes expired
copy and requests one repaint, with the already-active controller timer providing
a no-extra-repaint fallback if Win32 cannot create that timer.
A thin native host adapter now owns the bounded catalog identity projection,
consumes one complete bridge failure drain as one transition, selects exact
dashboard/open-widget feedback, and applies timer/invalidation callbacks once
per batch. Catalog replacement/removal, hide/show, and Stop use the same seam;
the renderer, HWND, and bridge transport remain outside it. Its deterministic
Release target feeds two widgets through one pump, proves offscreen isolation,
one-shot expiry and controller-timer fallback, rejects a late prior generation,
and prevents feedback resurrection after hide or Stop.
This behavior now also has production-process evidence through the real YT Music
widget, worker runtime, managed bridge, native client drain, painted host surface,
and Windows UI Automation provider; the worker remains the same process after
failure.

The renderer can retain bounded visible semantic geometry on demand, and a
pure native accessibility-tree builder combines it with exact runtime/snapshot
identity, active input-scope filtering, names, values, focus/state, Invoke
metadata, and Slider ranges. ActionSurface descendants are collapsed into one
semantic target. Collection stays allocation-dormant until the first UIA query. The
HWND now publishes the immutable open-widget tree through `WM_GETOBJECT` and a
free-threaded Windows UI Automation fragment provider. Button/ActionSurface
Invoke and Slider RangeValue enqueue bounded asynchronous requests; the UI
thread revalidates the exact widget/runtime/snapshot/scope/node/action tuple
before using the existing controller-input route. Slider writes are quantized
and coalesced latest-wins. Controller slider optimism now advances one host
presentation revision resolved before the projection key; rendered pixels and
the accessibility tree consume the same presented-value map through adjustment,
acknowledgement, and timeout. A real `IUIAutomation` client test covers provider
publication, traversal, names, physical screen bounds, patterns, and stale
runtime rejection and verifies that the RangeValue event and provider both
carry the presented numeric value. A separate projection-cadence contract proves
stable and animation-only paints do not rebuild, while transition completion
requests one final-geometry projection. The provider now diffs the last
announced immutable tree and coalesces structure, logical-focus, and closed
property events behind one posted window message outside paint. It covers
semantic names/help, enabled/
selected state, RangeValue state, and DPI-aware physical bounds; a real UIA
client-handler test receives structure, focus, property, and live-region
signals. Each root/fragment is also
bound to one HWND generation: explicit pre-destroy detach disconnects UIA,
clears message/action authority, and keeps old providers unavailable across
same-handle reuse. A real client retains original and rebound roots through an
actual `DestroyWindow` and observes element-unavailable results. Root focus/
visibility are UI-thread-published, focus loss does not target the custom root,
and root plus node resize/DPI bounds are announced.
UIA Invoke/RangeValue input now crosses native host, bridge, runtime, and SDK
with an explicit `AccessibilityAutomation` origin. Physical controller remains
the omitted compatibility default. The bridge accepts revalidated automation as
an ordinary action but never mints dashboard gesture authority for it; the
runtime rejects any mismatched authority reservation, while the worker and SDK
independently omit private gesture context. Broker gesture sequences are emitted
only after exact host activation succeeds. The adversarial runtime case invokes
a capability synchronously from an automation-origin dashboard override and
records no context, activation, provider call, or companion grant; a separate
denied-activation case proves no gesture metadata reaches the broker. Focused
Release results are WidgetSdk 84/84, WidgetRuntime 49/49, and WidgetBridge
46/46. Packaged AppContainer/UIA evidence
remains a ship-gate item rather than an inferred OS-boundary claim. The
canonical Release host build and native suites are green for this change.
The dashboard now exposes its exact painted title as a level-one heading. Static
controller guidance is readable non-live text, while transient action feedback
replaces it with one polite status live region. Title, help, status, and catalog
display names participate in the host projection revision, and a real UIA client
observes the heading/live properties plus a status-change event. Tray Focus and
Invoke requests resolve the revalidated stable widget ID in one state transition
instead of replaying up to 256 directional navigation commands. Open widgets now
publish one composite root named for the active page, with widget controls,
closed non-focus-stealing Back/Close commands, exact visible quiet help or live
status, and the visible tray. Switching focus regions changes one focus owner;
it no longer replaces the semantic surface.
Composite elements now carry a closed widget/host-shell/tray owner domain
through AutomationId, runtime identity, lookup, event diffing, and queued action
authority. Cross-domain raw-ID reuse is safe, duplicate same-domain publication
fails closed, and authors do not reserve shell prefixes. Root Back remains a
typed tray transition; nested Back now mirrors the managed pressed-B resolver
for current focus, focusless scope roots, focused Disabled/Busy suppression,
ancestor fallback, stale focus, and nested-scope isolation. Revalidation repeats
the same focus-aware decision before automation-origin dispatch, with no scope
fallback. The native mirror uses allocation-free recursion within the protocol's
bounded tree depth, so the `noexcept` publication path cannot terminate on an
allocation failure.
Focused Release coverage passes Slider Interaction 2086, renderer 4636,
accessibility tree 16, projection cadence 11, host semantics 32, event planning
11, and provider 149 checks. The isolated Release native aggregate passes 24/24,
and the canonical Debug build/test path is green. Legacy MSAA and packaged
Narrator evidence remain open, so full screen-reader support is not yet claimed.

The dashboard and open-widget tray now share one pure `ComputeTrayLayout`
result across painting and pointer hit-testing. The bounded visible window,
selected-slot centering, embedded tray band, below-preferred fallback, and edge
hit policy have focused native coverage. This removes the prior duplicated
geometry formulas and gives the host UIA fragment tree the same final tile
rectangles as pixels and pointer input.

The visible dashboard/open-widget tray now publishes those shared rectangles as
UIA ListItems with required single selection and Invoke. The immutable host tree
retains selected/focused/enabled state and a monotonically increasing host
sequence. Select and Invoke queue a closed `ActivateTrayItem` action with a
stable widget target; the UI thread rejects stale trees, exits reorder mode, and
uses one direct stable-ID tray state transition. The real UIA-client suite
discovers the ListItem and obtains SelectionItem, while direct provider coverage
verifies the typed queued authority. The dashboard also publishes its heading,
static help, and transient live status. The open-widget composite retains those
tray items alongside widget content and typed Back/Close/footer semantics.

DLV-105 makes that visible window explicit when a compact surface cannot fit
the complete catalog. `TrayLayout` reserves stable previous/next controls,
keeps the selected stable ID visible, and exposes the exact adjacent off-page
target without changing persisted order. Paint, pointer hit testing, and the
host accessibility tree consume the same control bounds. Pointer/UIA overflow
selection moves the tray window without entering widget content; ordinary
Left/Right still visits every enabled widget in catalog order. Visible UIA
ListItems report `PositionInSet` and `SizeOfSet`, while overflow buttons announce
their direction and hidden count.

DLV-106 makes tray focus the complete authority during a cold identity switch.
The last admitted widget snapshot may remain visually rendered until the
destination is ready, but its committed focus ID is cleared before that retained
frame. During the interval the host publishes only current tray semantics for
the selected destination; outgoing widget descendants cannot remain focused,
actionable, or present in UIA. The bounded presentation diagnostic records the
input owner, selected stable ID, rendered visual focus, and semantic focus on
each authority transition. The production-shaped Audio Mixer to 420-ms cold
Game Launcher fixture proves both retained and admitted frames stay tray-owned
until an explicit Up/A entry.

DLV-118 closes the remaining compact-tray catalog churn seam. The responsibility
map for the touched `OverlayApp` hotspot is:

| Concern | Before DLV-118 | After DLV-118 |
| --- | --- | --- |
| Catalog-to-shell transition | `ApplyWidgetCatalogChange` mutated `OverlayState`, persisted, and synchronized lifecycle directly, bypassing the normal shell transition owner. | Catalog inventory changes enter `ApplyStateTransition`; descriptor-only replacements still synchronize lifecycle and invalidate visible chrome. |
| Published tray frame | Open-widget diagnostics and paint recomputed layout independently, and catalog events could return before replacement pixels were committed. | One per-frame `TrayLayout` feeds the diagnostic and painted tray; a visible catalog event synchronously commits that invalidated frame before later pointer messages. |
| Verification | Deterministic layout cases did not use the installed eight-item production order, and the production switch fixture did not churn the catalog. | Release-hard layout/state checks prove complete overflow reachability at Audio Mixer, Network Controls, and Game Launcher extents; the eight-widget production fixture proves compact selection/overflow, rapid cycling, and add/remove republish with exact focus/order retention. |

Focused Release evidence passes `TrayLayoutTests` (283 checks),
`OverlayStateTests`, `HostAccessibilityTests` (34 checks), and
`OverlayTargetingTests` (64 checks). The production-host matrix passes all eight
transitions plus catalog add/remove; maximum complete-content timings were
2,717 us draw, 1,325 us commit, 1,531 us coordinated geometry, and 218 us
nonblocking motion commit. No widget tree, compositor, capture, public protocol,
or aggregate work was used.

The visible shell uses separate panel and dimming-backdrop windows on the active
external foreground app's nearest monitor. An outside backdrop click closes the
overlay. Both windows are topmost only while visible. Ordinary controller reads
use a visibility-scoped GameInput lease; confirmed foreground adds exclusivity,
while denied activation remains readable and diagnosed as background-shared.
Alt+Tab/external foreground loss closes rather than retargets the visible overlay. DPI, display
topology, work-area, and client-size messages recompute placement/resources;
reentrant DPI placement is coalesced. This is normal DWM windowing, not game
injection.

Ordinary visible controller state is read through GameInput background delivery
plus best-effort foreground exclusivity. Exclusivity suppresses other GameInput
clients only; XInput, Raw Input,
direct HID, Steam Input, and remapped virtual controllers remain outside a
normal desktop overlay's containment boundary. Arrow keys, Enter, and Escape
provide unadvertised navigation/select/back fallbacks. Mouse hit testing uses
the same clipped active-scope geometry as controller focus; backdrop clicks
close and visible declarative controls receive only semantic A/select.

Guide/Home shows or hides from any depth. Dashboard D-pad/left-stick movement,
A, B, and Y are host-owned; dashboard B closes the overlay. Open widgets receive
their other semantic actions; B falls back to the dashboard only when unhandled
in the root input scope. Nested scopes never bubble to the root.
GameInput is the primary Guide source. A removable XInput compatibility adapter
uses an undocumented ordinal only for drivers observed to omit Guide callbacks;
its 25 ms timer is dormant unless GameInput reports an Xbox 360-family device,
with the previous always-on path retained only if device tracking cannot
register. It is not a universal device-compatibility guarantee.

Open-widget left-stick navigation is two-dimensional with engage/release
hysteresis and bounded repeat. Focus uses explicit neighbors first and
deterministic rendered geometry as a fallback, without wraparound. Disabled
and Busy Buttons/Sliders retain focus but suppress action dispatch. Focused
Sliders consume horizontal input for bounded value adjustment and retain
Up/Down navigation.

### Widget platform

- `WidgetProtocol`: strict version-1 manifests and additive snapshot protocols
  v1–v13, deterministic JSON, stable IDs, focus validation, quick actions with
  optional typed control-operation metadata, Scroll/surface hints, absolute-
  value Sliders, images, closed semantic glyphs, explicit active controller
  scopes and snapshot correlation, and interaction state.
- `WidgetSdk`: typed Stack, Row, protocol-v8 ResponsiveGrid, Text, semantic
  CodeText, Button, Progress, Slider, Scroll, Spacer, Image, Icon,
  LoadingIndicator, and protocol-v7 ActionSurface authoring;
  controller-ready ToggleButton, Stepper,
  IconButton, Card, SectionHeader, StatusBadge, Divider, Alert, EmptyState,
  SegmentedTabs, Switch, ScopedDialog, SettingsRow, and bounded nested
  ActionSheet plus single-select Picker, Scrubber, MediaTile, AppTile, and Toast
  composites with stable semantic
  `gbar-*` theme hooks; button glyphs; focus/shortcut/state helpers; scoped
  shortcut routing; bounded latest-wins Slider coalescing; invalidation;
  runtime-integrated immutable `WidgetModel<TState>` snapshots/updates,
  non-paged resources, model-backed SingleFlight/Latest/Serial optimistic
  commands, bounded route navigation with exact-scope Back/return focus, and
  validated hierarchical/opaque-key IDs; bounded lifecycle-owned operation
  lanes and offset-paged resources; one 16-pending-item active-lifetime action
  FIFO shared by direct/quick/controller ingress with typed admission,
  slider-tail coalescing, cooperative lifecycle drain, late-failure events, and
  active input-scope correlation on every standard open-widget action;
  five-state lifecycle hooks/tokens; bounded, non-overlapping
  Visible/Interactive tickers; transport-neutral capability access; and typed
  audio/network/Bluetooth/recent-activity/app-library/media-session services,
  exact-port loopback JSON, write-only private secrets, durable private JSON
  state with revision/CAS, descriptors, DTOs, events, errors, and public
  deterministic state-test fixtures.
- `WidgetRuntime`: lazy out-of-process workers over bounded framed JSON with
  prompt typed action admission rather than provider-completion acknowledgement,
  protocol-v1 empty-ack/failure-name compatibility, explicit lifecycle
  transitions, per-start host-owned companion sessions, pre-process host-owned
  residency leases, timeouts, asynchronous action/process failure reporting,
  and limited restart. Installed/community workers
  require stable host-derived capability-free Low-integrity AppContainers,
  stripped environments, transactionally applied exact non-inheriting
  verified-file grants, host-owned schema-2 write-ahead DACL recovery with a
  global cross-process authority lock and persisted volume/file identities,
  PID-bound isolated
  pipes, and pre-launch Job Object process-tree accounting/UI/cleanup
  containment. Public custom workers use
  `WidgetWorkerBootstrap`, which validates host arguments, authenticates the
  optional broker before constructing the widget, attaches host services before
  creation, and owns cancellation and transport disposal.
- `WidgetBridge`: current-user-only native sidecar pipe, trusted plus installed
  and manifest-backed bundled catalog discovery, no-poll last-good catalog
  monitoring/semantic revisions, compatible-worker preservation and changed-
  worker retirement, worker forwarding, shared direct/quick/controller action
  admission with explicit inactive/saturated failures, generation-owned late
  action failures, controller input,
  host-owned mandatory community isolation selection, PID/identity/declaration-
  bound broker companions, single-use exact-operation dashboard gesture
  authority, per-session verified-content lease handoff, aggregate resident-worker/count admission with a separate Settings
  control-plane slot, invalidation/failure events, no-poll platform-appearance revisions,
  globally layered widget themes, bounded shell appearance, and computed GBSS
  styles.
  Request scheduling is now isolated behind one internal dispatcher that owns
  unique request IDs, the global admitted-request bound, per-widget FIFO tails,
  session-fatal cancellation, and a production-enforced two-second drain
  deadline. One closed typed classifier maps strictly decoded known requests to
  global or canonical widget keys; malformed and unknown requests receive no
  implicit widget scheduling key. Deadline-expired cancellation-ignoring tasks
  lose their request IDs, tails, and capacity slots but remain explicitly
  observed in quarantine until termination, with late reply and session-fatal
  publication suppressed. The server retains framing, decoding, the reserved
  Stop lane, catalog/client lifetime, replies, and the serialized write path.
  The existing per-client operation gate remains only a
  worker-lifecycle/residency mutex for coordination with idle unload and catalog
  retirement; it does not duplicate request admission or FIFO ordering. The
  server authority surface decreased from 1,380 lines (62,914 bytes) to 1,312
  lines (59,775 bytes), with the 327-line dispatcher and 118-line classifier
  replacing the server's
  semaphore, active-ID/task registries, ordering gate, per-widget tails, fatal
  exception slot, and scheduling continuation. Retained focused Release run
  `20260810T133940Z-18b2982e` completed in 21.5 seconds: WidgetBridge 51/51,
  WidgetWorkerHost 9/9, and the documentation contract over 52 Markdown files.
  Correction run `20260810T144506Z-bbd2dbe6` retained WorkerHost 9/9 and exposed
  one predecessor-failure regression in Bridge (51/52); after narrowing late-
  failure suppression to handlers started under dispatcher-owned drain, final
  focused run `20260810T145134Z-be1c9952` passed Bridge 52/52 in 14.3 seconds.
- `WidgetBridge` and the native declarative renderer preserve the distinct
  `loadingIndicator` render role (rather than treating it as a container) and
  understand protocol-v7 ActionSurface orientation, computed style, bounded
  layout, full-surface focus/hit/pressed geometry, and fail-closed unknown-kind
  behavior. Reduced motion keeps LoadingIndicator accessible but static.
- `WidgetBridge` and native layout also transport and validate protocol-v8 Grid
  semantics. Grid derives bounded row-major columns from final logical-DIP
  width while preserving child IDs/focus order; it never becomes a focus stop.
- `WidgetWorkerHost`: a packaged generic worker executable that loads one
  installed package's public concrete SDK `Widget` entrypoint and contained
  dependencies inside the mandatory package AppContainer, authenticates an
  optional broker channel, attaches typed host services before creation, then
  serves the standard isolated snapshot/action/lifecycle protocol.
- `WidgetStyling`: single-handle consumed-byte bounds, strict UTF-8 GBSS
  decoding, optional exact per-file SHA-256 inventories, closed typed source-
  failure results with stable sanitized diagnostics, safe package-relative
  imports, variables, explicit trusted cascade layers, typed allowlisted values,
  independent top/right/bottom/left border width/color overrides, and
  diagnostics. The built-in theme provides bounded `.gbar-code-text` wrapping
  with one Windows-baseline `Consolas` family; CSS-style font fallback stacks
  and packaged font loading are not claimed.
- `PlatformSettings`: strict atomic/cross-process appearance persistence,
  version-pinned development themes, built-in default, safe theme discovery,
  layer composition, last-good snapshots, and bounded declared-
  publisher/package-scoped public widget configuration under `widget-config`,
  with unambiguous owning-namespace resolution for unsigned runtime authorities.
- `WidgetCatalog`: safe `.gbarwidget` inspection/extraction, host-sealed content-
  tree integrity with single-handle bounded metadata reads and exact-length
  file hashing plus manifest bytes captured from the verified tree, complete
  relative-path/length/SHA-256 inventories, and bounded per-session launch
  leases that pin every verified file against write/delete replacement,
  version-addressed installs, schema-1 state migration, fail-closed
  exact version pins, discovery,
  enablement, and pin-preserving order persistence. Enabled compatible packages
  join complete validated live bridge revisions and remain lazy until first use.
- `PlatformBroker`: a version-1 audio/network/Bluetooth/recent-activity/app-library/media/
  exact-loopback/private-secret/private-state
  capability foundation with a closed versioned grant vocabulary, a separate
  Bridge-only non-consent state grant, SID/Low-
  label/PID-bound isolated endpoints plus nonce/
  identity authentication, manifest/consent/lifecycle enforcement, strict
  bounded DTOs/events, atomic consent persistence, bounded/coalesced
  subscriptions, and a deterministic simulator. It is connected to widget
  `HostServices`; the trusted bridge composes the real Windows audio, network,
  Bluetooth, foreground-activity, Start Menu app-library, media, and community-
  companion backends.
  DLV-031 keeps `PlatformCapabilityBroker` as the only identity, declaration,
  consent, lifecycle, request-lease, dashboard-gesture, revocation,
  event-sequence, and subscription authority. Its former per-operation switch
  and distant domain validators are replaced by seven explicit internal domain
  routes for audio, network/Bluetooth/activity, app library, media/Spotify,
  loopback, private secrets, and private state. The 2,377-line, 117,433-byte
  authority type is now 837 lines and 36,377 bytes. The existing app-library
  serialization gate/cache moved together into its handler; the broker state
  lock and loopback gate remain in the authority. No lock, task registry,
  cancellation source, lifecycle owner, request-lease owner, or sequence
  counter was added.
- `WindowsAudioProvider`: an event-driven Core Audio backend for sanitized
  per-application sessions on the current default multimedia render endpoint.
  A dedicated MTA owns native objects; callbacks only enqueue coalesced refresh
  work. It supports endpoint master volume/mute, per-session volume/mute,
  sanitized current default-device visibility, and volume/mute for the current
  default microphone. There is no undocumented default-device setter and no
  microphone sample capture.
- `WindowsNetworkProvider`: a lazy event-driven Windows backend with a dedicated
  MTA owner, bounded/coalesced queues, coarse IP Helper connectivity hints,
  ACM-only Native Wi-Fi notifications, explicit available-network scanning,
  generation-bound opaque result IDs, and saved/open result connection.
  Automatic status reads still do not query location-sensitive current
  SSID/signal, and no scan runs without an Interactive user action. Separate
  read/control grants expose software Wi-Fi radio state through
  `WlanQueryInterface`/`WlanSetInterface`; hardware/policy state remains
  authoritative and multi-PHY partial failure is explicit.
- `WindowsBluetoothProvider`: a lazy event-driven WinRT backend for sanitized
  Bluetooth software-radio state and bounded nearby/paired/connected discovery.
  It supports software radio On/Off plus explicit pairing and removal of one
  current opaque device through Windows Association Endpoint pairing. Removal
  uses its own destructive grant and always refreshes authoritative state.
  Unsupported ceremonies and profile-specific operations open the Windows
  Bluetooth Settings surface through a separately consented capability. Native
  device IDs never cross the broker. Generic device Connect/Disconnect is not
  implemented.
- `WindowsActivityProvider`: a lazy WinEvent foreground/destroy observer with
  no polling. It keeps at most 16 eligible running applications and publishes
  bounded display names plus per-process-lifetime opaque IDs; activation can
  switch only to a still-running observed window. It does not read UserAssist,
  launch executables, expose PID/path/HWND, or claim game classification.
- `WindowsAppLibraryProvider`: a lazy bounded current-user/all-user Start Menu
  `.lnk` catalog. It publishes sanitized names, conservative kinds, and random
  short-lived launch IDs plus broker-derived authority-scoped durable SavedIds;
  launch re-enumerates and requires one exact unchanged shortcut before
  invoking only the Shell `open` verb. Paths, raw stable identities, targets,
  arguments, AUMIDs,
  package identities, PIDs, and HWNDs never enter the widget contract.
- `WindowsMediaProvider`: a lazy, event-driven Windows Global System Media
  Transport Controls (GSMTC) backend. It publishes bounded sanitized sessions
  with broker-issued process-lifetime IDs, retains multiple sessions from the
  same source application, controls the exact selected session, and does not
  expose AUMID, PID, executable path, window handle, or raw platform objects.
- `WindowsCommunityProvider`: constrained JSON GET/POST to one declared
  nonprivileged IPv4 loopback port plus package-scoped write-only private
  secret slots. It disables DNS, proxy, redirects, cookies, decompression, and
  raw socket exposure; streams bounded strict-JSON responses; injects optional
  Bearer values inside the trusted provider; optionally removes the exact
  rejected scoped Bearer on HTTP 401 while its dependent lease remains valid;
  and stores hashed publisher/
  package/slot targets in Windows Credential Manager without returning values
  to widget IPC. It also owns the separate private-state backend: one strict
  canonical 64 KiB JSON document/tombstone per authenticated publisher/package,
  cross-process serialization, atomic replacement, revision/CAS, persistent
  mutation throttling, and corruption/reparse failure closed.
- `GbarCli`: working `new`, `validate`, `render`, `replay`, deterministic
  `pack`, bounded local/HTTPS/GitHub Release `install`, and catalog `list`,
  `enable`, `disable`, and `version list|select|rollback` commands. Local and
  remote updates share an exact-stream pre-publish enabled-ID guard. Remote
  acquisition requires SHA-256 pinning, reports the actual digest, and installs
  disabled pending explicit review. `gbar new` emits the matching SDK as a
  project-local offline NuGet package, clears external feeds, and generates a
  lifecycle/state/action snapshot test without an absolute checkout reference.
  Source-aware `gbar pack` reuses the bounded isolated dev build/validation
  path, stages only runtime output without compiler symbols, and preserves the
  raw deterministic directory packer for advanced staging. `gbar dev` provides a bounded unsigned
  source/package watch-build-run loop through the production generic worker,
  AppContainer, broker, lifecycle, renderer, and Settings permission path. A
  controller/hotkey-free candidate must authenticate its exact catalog/widget/
  instance and return a validated snapshot before replacing the last-good
  interactive generation; failed generations retain or restore last good.

Audio Mixer, Network Controls, Games & Apps, and Now Playing are manifest-backed
`bundledWidgets`: they use the same generic `WidgetWorkerHost`, package-specific
capability-free AppContainer, authenticated capability broker, lifecycle,
renderer, and manifest-derived authority as an independently installed
community package. Their host catalog entries supply only platform-owned shell
presentation identity and package location. A Windows Release conformance suite
packages, installs, enables, resolves, launches, renders, and acts through that
same public path for all four. YT Music is a separately installable Community
package on that same generic AppContainer path; its conformance case builds the
real `.gbarwidget`, installs/enables it through the public catalog, drives
pairing and dashboard transport through the broker, and asserts there is no
trusted catalog/worker fallback. Settings is the only temporary trusted Job-
only exception. The Clock sample also exercises the public package path.
Audio Mixer and Network Controls are bounded integration slices for the larger
controller-first Audio Control and Network Control roadmap items; their
presence here does not mean those product widgets are complete or shipped.
Settings renders through
the generic SDK/bridge/native path, uses nested controller scopes, persists
bounded appearance values, pages valid/invalid themes, exposes diagnostics,
requires confirmation before reset, and provides two separate controller
flows: a source-separated widget inventory with read-only Built-in manifest
details and an honest Community review that labels packages unsigned, treats
the manifest publisher as unverified, shows the full sealed SHA-256 digest,
and repeats that digest at consent; a nested paged Manage versions surface with
digest prefixes and disabled-only exact selection/rollback; plus enable/disable
for Community packages only; then package → capability → grant/deny consent. Enablement is
not consent. Permission grants require explicit
confirmation; deny/revoke is immediate, missing/invalid state fails closed,
and first-party packages are not auto-granted. An exact tombstone migrates the
retired Recent Apps activation decision without discarding current consent;
arbitrary unknown capability IDs still invalidate the document and fail closed.
Unsupported declarations and inactive saved decisions now appear behind one
focusable Review row rather than inline text. Its nested read-only controller
Scroll uses B-only return, stable opaque IDs, sanitized bounded labels, one
shared 16-item detail budget, and exact publisher-authority distinction. It
classifies inactive decisions only when catalog projection and consent are
valid and complete; otherwise it explicitly reports classification unavailable
and publishes no inactive rows. It never removes or rewrites consent.
It reloads settings/themes/
catalog/permissions once per active lifetime and does not poll in Background.
The same public compatibility evaluator gates Bridge and Settings: details show
host API/architectures and a bounded reason, incompatible enablement is blocked,
and disable remains available for recovery.

The current Audio Mixer reference slice is packaged and registered through the
same catalog/bridge/worker path as the other widgets. Its widget code uses only
the public typed audio service,
fetches once on activation, then
reacts to provider events rather than polling. It offers controller session
selection plus optimistic per-session volume/mute controls in Interactive and
renders explicit permission, lifecycle, empty, unavailable, and failure states.
When its dashboard card is selected, the snapshot also advertises three visible
master-output prompts: LB/RB lower or raise volume by a clamped five percentage
points and X toggles mute. Each prompt names the current value/state and carries
only the exact existing output-volume or output-mute operation. The host and
broker still require current selection/generation, physical input, declaration,
consent, Visible lifecycle, one unexpired single-use sequence, and an exact
operation match. Rapid volume input reuses the widget's existing latest-target
coalescing; provider events and a bounded reread own confirmation, while failure
restores the authoritative value with sanitized feedback. Opening the widget
uses that same committed output state and preserves the Slider's D-pad/A
behavior; application sessions and microphone controls receive no dashboard
authority.
The worker capability adapter now treats invocation start as the exact gesture
admission boundary: once the host has granted the matching sequence and
operation, completion of the worker/host activation round-trip cannot downgrade
that already-started request merely because the widget action scope returned.
The grant remains single-use and is still unavailable to any later invocation.

Focused dirty-worktree Release run `20260811T003239Z-60b7c837` completed in
86.088 seconds and passed Audio Mixer 45/45, Platform Broker 51/51, the installed
generic AppContainer worker/bridge/broker route 6/6, and documentation validation
across 52 Markdown files. The run retained one stable dirty-status fingerprint
at accepted control-plane commit `a1b9527`; no aggregate, native, Windows Audio
provider, or unrelated suite was run.
One root controller Scroll contains master, sanitized device summary,
microphone, and every application row; this replaces the clipped nested session
viewport and gives `audio.root` one stable host-owned offset/focus-follow
surface at normal and constrained heights. One explicit Up/Down chain reaches
the true master-output top, optional microphone control, and every application
through the final bottom row. The widget remembers the last focused master,
microphone, or stable application target from controller input; reopen restores
that target, and session churn falls back to the nearest surviving row instead
of jumping to master. It also shows sanitized current
default output/input device names and offers controller sliders/mute for the
current default microphone behind independent optional grants. Endpoint
selection remains display-only because no supported system-default setter has
been adopted.
Broader hardware/churn coverage and end-to-end hidden/visible performance evidence
remain open; this is not yet an end-user release claim.

The DLV-029 and DLV-042 Audio Mixer splits keep `AudioMixerWidget` as the only
committed-state, selection, action-routing, status, and invalidation owner.
Before DLV-029, its roughly 2,496-line class also owned complete view composition
and the nested output/input/session pending-command mechanics. DLV-029 moved the
closed command transitions into a 354-line internal policy boundary and the
unchanged controller view/focus graph into a 351-line snapshot-only presenter,
leaving a 1,880-line root. Before DLV-042 that residual root still owned the
linked Active lifetime, four subscription startup paths and event pumps, initial
snapshot ordering, two retry semaphores, two optional-attempt cancellation
slots, capability failure classification, and provider-task shutdown.

DLV-042 moves that complete provider lifecycle and ingestion responsibility
into one internal `AudioMixerProviderSession`. The session owns one linked
Active lifetime, all four subscription-before-snapshot paths, one root task and
its locally bounded pumps, optional-section attempt replacement/retry signals,
safe failure classification, cancellation, and terminal drain. It publishes
only typed immutable snapshots, events, loading states, and bounded failures;
the widget accepts them only from the exact current session and applies them
under its existing state lock. The session owns no committed widget state,
focus/action policy, command transition, view composition, status copy, or
invalidation. The root is now roughly 1,687 lines and has no provider retry
semaphore, provider attempt cancellation source, provider pump, or subscription
startup method. Coordination crossing the boundary is one current-session
reference, its cancellation token for the already widget-owned command tasks,
typed immutable observations, `Retry`, and terminal `StopAsync`; no mutable
collection or render state is shared.

The residual root's cohesive exception is now precise: it is the single
transactional application owner that admits actions, invokes the six audio
control operations, applies closed command-policy and provider-observation
results to one committed model, preserves selection/focus intent, publishes
status, and invalidates. Moving those remaining effects would require either a
second committed-state/lock/invalidation owner or an additional command-task
coordinator, both of which this split intentionally avoids. The presenter,
command policies, and provider session remain internal and add no public SDK,
protocol, broker, provider, native, endpoint-selection, focus-ID, navigation, or
shared-Scroll change. Direct deterministic coverage now includes every DLV-029
command transition plus all four subscription-before-fetch paths, buffered gap
events, optional failure/retry isolation, required failure and stream closure,
cancellation-ignoring late result/event drain, replacement-session rejection,
reactivation, and exact ownership reflection. The assigned focused Release
group covers Audio Mixer, Widget SDK lifecycle/subscription, generic worker
lifecycle, Windows Audio provider, and documentation contracts; no aggregate
or native suite is required. Bounded dirty-worktree Release run
`20260810T162728Z-cb2f0ef9` passed Audio Mixer 41/41, Widget SDK 84/84,
generic worker 9/9, Windows Audio provider 15/15, and documentation validation
across 52 Markdown files in 16.565 seconds. This is assignment-scoped
implementation evidence, not a canonical aggregate or release-eligible clean-
worktree result.

The current Network Controls reference slice runs as a manifest-backed bundled
package through the generic community worker/AppContainer path. It requires
coarse network and available-Wi-Fi read grants,
makes current saved/open result connection optional and Interactive-only,
opens acknowledged status and available-Wi-Fi subscriptions before snapshots,
and never polls. It declares no dashboard quick actions. The open widget has
separate Wi-Fi and Bluetooth views under a segmented tab bar. LB/RB switches
the active view from any root focus, while D-pad/left-stick navigates within the
active view. Wi-Fi and Bluetooth use the independent stable Scroll IDs
`network.wifi.body.scroll` and `network.bluetooth.body.scroll`; each retains its
own opaque selected item so returning restores the focus target and lets host
focus-follow restore its visible location. In Wi-Fi, A starts one explicit scan
from the Scan control and A or X routes a connection through the exact focused
row. LT/RT never cycles list items.
The shared segmented-tab container now has a nonshrinking 50-DIP minimum region
around 44-DIP tab targets. Native layout regression scenarios at the normal
compact viewport and a constrained high-interface/high-text-scale viewport
verify both tabs remain visible, controller-enabled, inside the viewport, and
inset far enough for an unclipped focus border.
The real provider returns on `WlanConnect` acceptance, then publishes
authoritative `Connecting`, `Failed`, and refreshed status events. Its focused
provider/widget suites and the complete Release verifier pass at milestone
`8e8c90a`. Focused checks do not replace hardware/privacy/performance matrices,
which remain open. The current full managed Release suite, native host suite,
package required-file checks, hidden-startup smoke, and controller input-probe
smoke pass.
Available-network broker/SDK contracts and the explicit, event-driven Native
Wi-Fi scan/connect provider are declared and rendered by the bundled widget.
They use generation-bound opaque IDs and cover saved-profile-backed and
unsaved-open connection starts with dedicated broker/provider/widget tests.
Host-owned WPA2/WPA3 Personal credential entry is implemented for the exact
current bundled Network Controls generation. The masked native modal bypasses
the worker; the provider creates one per-user profile without overwrite,
correlates the existing ACM completion, and removes only its newly created
profile on terminal failure. Software Wi-Fi radio
read/control, Bluetooth radio/discovery, explicit pairing/removal, and a
separately consented Windows Settings management fallback are implemented
behind granular grants. Removal requires an ActionSheet confirmation, a current
paired opaque identity, Interactive lifecycle, and authoritative disappearance
before success. Generic Bluetooth Connect/Disconnect remains outside the
current broker surface, and physical remove/re-pair still needs reversible
hardware verification.

DLV-091 adds the separately consented
`system.network.bluetooth.unpair.v1` operation. Network Controls binds an
explicit destructive ActionSheet to one current paired opaque row, cancels or
rejects stale/lost-Interactive requests, and reports success only after the
trusted provider's authoritative refresh removes that pairing. Focused Release
evidence passes Network Controls 22/22, Windows Bluetooth 18/18, PlatformBroker
52/52, WidgetSdk 87/87, SDK compatibility 12/12, Settings 54/54, WidgetBridge
74/74, WidgetRuntime 74/74, and documentation validation across 55 Markdown
files. The installed generic-AppContainer route passes 6/6 with cancel,
confirmation, exact removal, stale-row rejection, and unaffected-neighbor
coverage; retained result:
`artifacts/verification/20260811T191258Z-5004ee02/verification-result.json`.

DLV-093 replaces the protected-Wi-Fi JSON credential member with one bounded
raw secret frame owned by mutable native bytes and managed characters. The
masked edit control is overwritten before destruction; native serialization,
managed parsing, provider-command, pinned profile XML, and rejected/failed-read
owners clear at their terminal boundaries. Tests retain only generated mutable
inputs and derived sentinels. Temporary profiles now use random attempt-unique
names plus 32-byte per-profile custom user data. The provider verifies that
token before connect and again before rollback, never overwrites a profile,
never deletes on missing/replaced/unreadable ownership, exposes typed rollback
outcomes, and clears the temporary tag after successful connection. Focused
Release evidence passes Windows Network 55/55, Network Controls 22/22, and
WidgetBridge 76/76. The final provider result is
`artifacts/verification/20260811T201025Z-1ccb30f2/verification-result.json`;
the retained Network Controls/Bridge group is
`artifacts/verification/20260811T200911Z-98b11010/verification-result.json`.
The installed generic-AppContainer conformance seam passes 6/6 at
`artifacts/verification/20260811T200444Z-b424f5c7/verification-result.json`.
The focused native build compiled the production host and directly passed
`WidgetBridgeCatalogTests` plus `TextEntryModalTests`; the broader native group
then stopped in the unchanged production-host fixture because OverlayHost did
not create a visible HWND in time. Retained result:
`artifacts/verification/20260811T200143Z-4081e739/verification-result.json`.
That later fixture limitation is not represented as protected-Wi-Fi acceptance
evidence and is not rerun here.

DLV-092 adds a separately consented `system.network.details.read.v1` read
capability and a controller-first Connection details route. The trusted Windows
provider selects the documented best IPv4 interface when it can do so exactly,
filters loopback/link-local/temporary addresses, bounds IP/gateway/DNS display
values to 8/4/8, and reports offline, constrained, ambiguous, privacy-denied,
and unavailable states without exposing interface GUIDs, MAC addresses, route
tables, traffic, or Wi-Fi identity. Existing IP Helper/connectivity callbacks
feed one bounded revision-only invalidation lane; Network Controls re-queries
through an Active latest-wins operation, so event bursts coalesce, stale
cancellation-ignoring results cannot publish, and Background owns no polling
loop. Denial or failure remains isolated from Wi-Fi and Bluetooth controls.
Focused Release evidence passes Windows Network 57/57, PlatformBroker 53/53,
WidgetSdk 87/87, SDK compatibility 12/12, Network Controls 24/24, Settings
54/54, and documentation validation across 55 Markdown files. The grouped
results are retained at
`artifacts/verification/20260811T203708Z-7c7c9917/verification-result.json`
and the final provider/widget/docs delta at
`artifacts/verification/20260811T204118Z-957d5854/verification-result.json`.
The installed generic-AppContainer route passed 6/6 with exact consented
details projection and one revision-driven refresh at
`artifacts/verification/20260811T203857Z-1e230efe/verification-result.json`.
A later repetition after removing an unused internal test counter stopped on
the inherited Game Launcher fixture's all-Unavailable catalog rows; Network
Controls was unchanged by that removal and the unrelated failure is retained at
`artifacts/verification/20260811T204150Z-2e2e8009/verification-result.json`.

DLV-035 keeps `WindowsNetworkPlatformBackend` as the only native-adapter
lifetime, MTA owner-thread, command-queue, committed provider-state, event-
channel, subscriber-publication, and disposal owner. Before the split, its
1,186 lines (51,201 bytes) also contained the complete closed command
vocabulary and native-result mapping, both timeout state machines, opaque-ID
allocation and snapshot normalization, equality/duplicate suppression, and
broker-event construction. The root is now 836 lines (34,189 bytes). Command
execution is a 139-line internal policy, bounded admission and draining are a
275-line internal owner, owner-thread connection/scan
transitions are 96 lines, native-state reconciliation is 258 lines, event
projection is 21 lines, and the injected one-shot deadline mechanism is 19
lines.

The extracted owners add no thread, lock, task, channel, semaphore,
cancellation source, capability, or public API. The root retains one state lock
and one start lock, one bounded 128-entry command queue, one coalesced native-
outcome queue, three single-value event channels/pumps, and the same two
one-shot deadline schedulers. The admission owner reserves four physical queue
entries from the fixed bound, so ordinary admission stops at 124. Because a
disposed one-shot timer can already have a callback queued, an additional
bounded projection retains only the highest overflow generation for connection
and scan. It preserves the original cross-type arrival sequence across failed
promotion attempts and promotes a value only into a newly available FIFO tail
position. Arbitrarily many delayed stale callbacks therefore cannot consume
unbounded memory, displace the current deadline, or make a newer generation
inherit an older signal's position. Reconciliation and transient-operation
policy are invoked only by the MTA owner and return bounded values that the root
commits atomically. Timer callbacks no longer mutate connection state directly
or block on queue pressure. They admit generation-keyed commands through a
packed closed/count gate, and owner consumption or terminal draining releases
each admission exactly once. The gate uses a non-disposable completion signal,
so a producer finishing after the bounded close wait cannot publish into a
disposed queue or fault a timer callback. Stale deadlines remain rejected in
the single owner-thread order.

Direct credential-free policy fixtures cover typed command results, public-ID
admission, provider outcome/mismatch, current and stale connection/scan
deadlines, provider disappearance/reappearance, bounded label projection,
opaque-ID generation, duplicate projection, and the closed event vocabulary.
The composed provider cases additionally fill all 124 ordinary slots and prove
current connection and scan deadlines still reach terminal state exactly once;
delay more than four stale callbacks for each operation type and prove the
current generation survives bounded overflow; preserve cross-type arrival
order across failed promotion; place stale and replacement deadlines at their
distinct FIFO positions; and hold ordinary and deadline producers beyond the
bounded close wait to prove exception-free callback completion, zero queued
commands, zero overflow values, and zero admission counters. They retain
cancellation, radio rollback, provider churn, degraded recovery, late callbacks,
deadline failure, and owner-thread disposal coverage. Broader live hardware/
privacy/performance evidence remains open.

Bounded dirty-worktree Release run `20260810T171344Z-feced08e` passed Windows
Network provider 36/36, PlatformBroker 51/51, and documentation validation
across 52 Markdown files in 22.469 seconds. Its starting and finishing commit
and dirty-status fingerprint are identical, so it is stable assignment-scoped
implementation evidence rather than release-eligible clean-worktree evidence.
No aggregate, widget, native adapter, or OverlayHost suite was run.

Bounded correction run `20260810T175643Z-9adefc14` passed Windows Network
provider 42/42 and documentation validation across 52 Markdown files in 18.537
seconds. The run retained the same `51a6ec4` commit and dirty-status fingerprint
from start to finish. It is focused dirty-worktree evidence for ordinary and
deadline capacity, greater-than-four delayed callback overflow, cross-type FIFO
promotion, replacement ordering, and admission-versus-disposal balance; no
PlatformBroker, aggregate, native adapter, or OverlayHost suite was repeated.

DLV-041 preserves `WindowsNetworkNativeAdapter` as the only WLAN/IP notification
lifetime, three-handle, three-callback, adapter-generation, event-publication,
recovery, and disposal owner. Before the split, that owner was 1,297 lines
(53,028 bytes) and also contained the raw native-call surface, managed interface
projection, every WLAN list parser and profile/scan/connect transition, and the
multi-PHY radio transaction. The root is now 404 lines (14,528 bytes). A 438-line stateless
native-call facade owns P/Invoke and temporary call-local buffers; a 107-line
connectivity policy projects managed facts and best-route selection; a 537-line
WLAN policy owns only bounded profile, scan, available-network, and pending-
connection values; and a 216-line radio policy owns strict buffer projection
and compensating transactions.

Coordination remains one adapter-owned gate, moved from scan-only state to the
complete three-handle registration/use/cancellation, WLAN policy/callback,
event-publication admission/drain, and terminal-close boundary. Trusted event
handlers are invoked outside the gate. Disposal marks the lifetime terminal
inside the gate, suppresses admitted callbacks that have not begun publication,
and waits for already-committed publications from other threads. Active,
disposing, and terminal states keep concurrent external disposal callers from
returning before exact-once cleanup and drain. Reentrant handler disposal
excludes its own already-started publication from that wait, and the handler's
final unwind transitions the lifetime to terminal for external waiters.
The extracted policies and
native-call facade add no thread, task, lock, channel, semaphore, cancellation
source, handle, callback registration, generation, disposal authority, public
API, capability, or protocol field. The adapter passes its current handle into
a policy call only while holding that gate; policies return bounded immutable
snapshots, typed results, or callback projections. Deterministic injected-call
fixtures prove singular open/register/recovery/unregister/close counts, failed-
open recovery without duplicate handles, generation-bound scan and connection
callbacks, recovery racing disposal without post-terminal registration, an
admitted callback racing disposal without post-terminal publication, reentrant
handler disposal, two concurrent external disposal callers waiting for the
same terminal drain, exact once handle cancellation/close, strict interface/SSID/
radio bounds with complete buffer release, and compensating radio rollback.

Bounded dirty-worktree Release run `20260810T183528Z-b4084bd8` passed Windows
Network provider 48/48, PlatformBroker 51/51, and documentation validation
across 52 Markdown files in 25.906 seconds. Its starting and finishing commit
and dirty-status fingerprint are identical. This is stable assignment-scoped
evidence for the managed native-adapter boundary, injected interop fixtures,
and unchanged broker mapping; no aggregate, live-hardware, native OverlayHost,
or unrelated widget suite was run.

Bounded DLV-041 correction run `20260810T185231Z-21f21f42` passed Windows
Network provider 51/51, PlatformBroker 51/51, and documentation validation
across 52 Markdown files in 24.827 seconds. The retained run kept commit
`3e6d779` and its dirty-status fingerprint stable. It adds deterministic
recovery-versus-disposal, admitted-publication-versus-disposal, concurrent
external disposal, and reentrant handler disposal evidence; no aggregate,
live-hardware, native OverlayHost, or unrelated widget suite was run.

Games & Apps has replaced Recent Apps in the bundled catalog and first-party
package conformance path. It is an ordinary public-SDK package in the generic
AppContainer, requires `system.apps.library.read.v1`, optionally declares the
separate `system.apps.library.launch.v1`, and loads bounded 32-item pages. Its
default Library automatically appends trusted `Game` entries up to the existing
64-SavedId curated limit. Applications and Unknown entries are available only
through the nested vertical Catalog. Library and Catalog entries are full-width single-focus rows; curated
rows can place the bounded trusted PNG inside that same Button target. A
toggles Catalog membership, X removes from Library, and one
confirmed launch moves that exact item to the front. Curation, selected item,
automatic provenance, exact Game exclusions, recent-first order, and bounded
display-only last-good copy persist across worker restart/unload in
`HostServices.PrivateState`. AppIds never persist. Activation shows the saved
display immediately, then reconciles the bounded catalog and resolves curated
entries to fresh short-lived launch tokens; checking or missing rows remain
disabled while the cached Library and its single Add applications action stay
visible. Remove operations publish only after private-state commit, and a
failed write keeps every prior row; bounded CAS conflict recovery applies the
exact delta without dropping unrelated display projection. Removal of a Game
persists an exclusion, same-identity reappearance stays excluded, and explicit
Catalog addition clears it. Launch is
enabled only while Interactive and targets only the opaque ID associated with
the exact action source. Permission, lifecycle, healthy-empty, unavailable,
stale-item, and generic failure states remain controller reachable and
sanitized. A successful provider result emits one generation-bound host effect
that closes the overlay only after the exact provider launch succeeds; failure,
denial, cancellation, or a stale widget generation keeps it open.

Initial and explicit-retry library reads run in the SDK's runtime-owned Active
operation lane, so leaving the widget cancels and drains provider work before
the lifecycle transition completes. Toast expiry likewise has one disposal
owner and removes its shared cancellation reference before disposal;
deactivation cannot race an already-disposed source. The focused suite covers
cancellation during retry, and the real generic-worker/AppContainer path passed
three consecutive lifecycle conformance runs.

DLV-059, corrected by DLV-071, replaces the provider root's Start
Menu/AppsFolder/Steam discovery,
launch, and artwork switches with two ordinary implementations of one private
normalized source contract. The 475-line authoritative owner keeps the sole
scan gate, immutable merged snapshot, opaque public-ID map, icon cache, and
terminal lifetime. That lifetime is linked into every admitted source/Shell
operation; composite broker shutdown cancels it, joins the scan gate's bounded
cooperative publication drain (or reports its bounded timeout), disposes every
source exactly once, clears retained
authority/artwork, and publishes one shared terminal result to concurrent or
repeated disposers. A
564-line source-policy file owns the shared bounded generation/disposal policy
plus the Windows-installed and Steam adapters; each adapter alone knows its raw
resolution and launch authority. Cross-boundary mutable knowledge is limited to
immutable normalized records carrying opaque source/record identities,
sanitized attribution, kind, installed/available state, supported actions,
artwork revision, health, and source version. Direct fixtures prove exact
source-owned resolve/launch, duplicate-name separation, last-good failure
isolation, a late generation losing to the current snapshot, and cancellation-
driven terminal drain. The corrected focused Release suite passes 41/41,
including production composite disposal, active cooperative cancellation,
cancellation-ignoring late completion, bounded uncooperative failure, all-source
cleanup after one failure, post-terminal rejection, and concurrent idempotent
disposal. No widget, native, aggregate, or physical launch verification ran.

The trusted `WindowsAppLibraryProvider` lazily merges the bounded current-user/
all-user Start Menu Programs roots, the current user's Shell AppsFolder, and
registered Steam libraries on one process-wide bounded STA lane. It skips
shortcut reparse points, accepts canonical AUMIDs and bounded Steam manifests,
deduplicates trusted descriptors, classifies only reviewed Steam registrations
as Games, and exposes only sanitized names, conservative kinds, short-lived random launch IDs, and broker-derived
authority-scoped durable SavedIds. Raw paths, AUMIDs, stable provider identity,
arguments, and returned activation PIDs remain host-only. Before launch it
re-enumerates the exact source. A shortcut must retain its scope, target,
path, and fingerprint before constrained Shell open; an AppsFolder entry must
retain exactly one canonical AUMID/revalidation identity before null-argument
`ActivateApplication`; Steam launch revalidates the exact numeric AppId,
manifest location, and content before opening its constrained URI. For curated
entries it rasterizes the trusted shortcut or AppsFolder Shell icon into a
bounded inline PNG; broad Catalog discovery stays text-only. Additional
launcher libraries and classification sources remain open. Recent Apps remains only as a read-only
foreground-activity API/test reference and is not packaged in the current
overlay. See [Games & Apps](games-and-apps.md).

DLV-072 replaces the pre-release 512-entry offset snapshot with one managed
app-library cursor contract. The normalized provider alone owns the complete
catalog (bounded at 10,000 records); broker and SDK responses contain at most 64
sanitized rows, opaque revision/query/page-size/direction-bound cursors, and one
sanitized source label per row. The capability domain retains at most 256
current launch IDs and artwork registrations, invalidates them on provider
revision change, and projects only requested matches after a bounded SavedId
scan so resolution cannot evict its own fresh launch authority. Games & Apps now
uses the same forward/reverse cursor service while retaining only its current
32-row Catalog page and bounded curated state. The old public offset request,
central 512-row broker snapshot, and compatibility facade are removed; this is
an intentional breaking pre-release WidgetSdk baseline update.

Now Playing is the public-SDK media-session reference. It uses only the typed
`HostServices.Media` surface over the authenticated broker and the event-driven
GSMTC provider; the worker cannot open GSMTC directly. It retains duplicate
sessions from one application through opaque broker IDs, preserves the selected
session across complete snapshots, renders a compact controller surface with a
bounded host-normalized GSMTC thumbnail when the media app publishes one, and
interpolates active progress locally at four Hz between authoritative events.
All render-facing session, selection, pending-command, view/status, revision,
live-update, and reload state now resides in one `WidgetModel<State>`; a focused
regression proves one invalidation for a changed selection and none for a
repeated equal selection. Command reconciliation remains widget-owned rather
than inferred by the SDK, but SingleFlight admission, lifecycle-owned provider
execution, current-attempt completion, safe error fallback, and rollback
sequencing now use `WidgetOptimisticCommand`. Play/Pause projects immediately;
rollback restores only the affected session when its generation/revision is
still current.
X/LB/RB quick actions expose play-pause/previous/next while the card is merely
Visible. Each quick action names the one media control operation it may invoke;
the bridge first records a dormant host-owned reservation for at most 10 seconds
so bounded serial widget work can reach the exact call. That reservation is not
broker authority. Invoking the exact typed operation atomically activates one
identity/PID/snapshot/input-sequence-bound broker lease for at most two seconds.
It is consumed once without promoting lifecycle or enabling subscriptions.
Normal declaration, consent, payload, and provider checks still apply.

YT Music is the first Community addon integration reference. It is absent from
the trusted/bundled host catalog and runtime-copy list, and its retired custom
desktop worker/Credential Manager adapter are removed. The build helper stages
the real manifest, assembly, and GBSS, then uses public `gbar validate`, `pack`,
`install`, and `enable` commands. The generic package AppContainer accesses
YTMDesktop2 only through declared `network.loopback:13091`; optional
`storage.private-secrets.v1` persists pairing without returning a token to the
worker. Host-side bearer injection requires both grants. The addon performs a
request-scoped host-side invalidation of the exact rejected Bearer slot on HTTP
401, clears only its local connection cache, and never races a second delete.
It performs a non-blocking automatic connection attempt and renders media
metadata, artwork, transport state, and
dashboard quick actions through the declarative protocol. It auto-connects on
entry into Visible/Interactive when disconnected, interpolates progress there at four Hz,
reconciles the companion every two seconds, and uses bounded optimistic transport/rating
updates with rollback on command failure. Like, dislike, shuffle, and repeat
publish immediate semantic selected/busy feedback, preserve independent pending
features through stale polls, and clear or roll back on reconciliation.
DLV-009 removes the widget's three lifecycle task fields and its hidden
auto-connect admission flag. SDK-owned Active operation lanes now own and drain
auto-connect, progress, polling, and latest-wins transport work. One immutable
presentation record supplies every render input, and current-attempt checks
reject cancellation-ignoring late pairing, polling, and transport completions.
Accepted play/pause commands reconcile in a bounded Active-lifetime operation
rather than the shorter input-action lifetime. The completion test inspects
pending authoritative confirmation instead of the merged optimistic snapshot,
and repeated toggles supersede the earlier refresh burst without blocking.
Its connected X/LB/RB/Y window shortcuts are declared once on the active root
scope, so they resolve from every connected focus target without sibling
searching and remain isolated from nested scopes. Previous/Next use corrected
native directional geometry.

The overlay now treats the selected widget panel and icon tray as persistent
sibling regions. Tray selection swaps the Visible panel automatically; A moves
focus into its controls and publishes Interactive; root B or a root Down
boundary returns focus to the still-visible tray; tray B closes; and nested
scopes remain contained. Guide is a region-independent global toggle. The
responsive recovery path also accepts an empty current focus: it selects the
first visible root control when possible, and a fully unreachable root returns
to the tray on Down while a clipped nested scope stays contained. The built-in
themes also use non-shrinking fixed regions, a thin native Slider
 track inside the 44-DIP target, and lighter typography/radii/spacing. Pressed
 Up from the tray now enters the visible widget. Scroll focus-follow snaps the
 first and last focusable descendants to the true extent boundaries. The
 host resolves focus-edge pagination against the current row before an ordinary
 move can leave its Scroll, and a changed replacement-page focus request
 outranks stale ordinal focus memory. That request is consumed when focus is
 remembered, so an unrelated refresh cannot steal focus again. Deterministic
 compact and expanded Spotify coverage now drives the real 29-item playlist and
 detail resources through the 12/12/5 forward/reverse sequence, including a slow
 cancellation-ignoring page, joined repeated edge input, cached reverse pages,
 absolute visible IDs, and exact provider call counts. Matching native tests use the
 exact Spotify scroll/rail IDs and five-row final topology. Focused Release
 coverage passes 24 widget-surface focus checks and 47 focus-navigation checks.
 DLV-006 adds protocol-v14 continuous cursor collections without changing the
 offset-paged replacement-window contract. `WidgetCursorResource<T>` owns one
 latest-wins bidirectional request lane, complete-page append/prepend, a
 two-page/256-item retention ceiling, 256 remembered cursors, stable typed item
 keys, anchor-aware refresh, safe nearest deletion fallback, and exact entering-
 edge focus. Native layout retains the authored anchor's viewport-relative
 position before the first paint and keyed List/Grid descendants drive the
 existing near-edge actions before focus can escape to a fixed header. Opaque
 artwork handles are parsed and preserved as bounded identities but grant no
 URL, file, network, decode, or action authority. DLV-018 completes the trusted
 application-artwork route: Start Menu and AppsFolder registrations issue
 generation-bound opaque handles. DLV-054 makes private native demand a quick
 correlated admission followed by bounded asynchronous provider work, so input,
 lifecycle, catalog, and snapshot traffic remains live while an icon source is
 blocked. Completion rechecks the current worker and artwork generations. A
 host-only digest of the provider's exact revalidation record rotates the handle
 and per-row decoded/bitmap key when trusted icon content changes without a
 SavedId or launch-identity change. Native demand remains lazy with deterministic
 32-entry / 32 MiB in-memory eviction and no disk cache; missing, malformed, stale,
 replaced, or Steam registrations without a valid bounded local cache asset retain the semantic
 fallback without changing launch, focus, membership, or warm-start identity.
 Focused DLV-018 Release evidence passes 32 Windows app-library provider,
 51 broker, 56 Games & Apps, 70 production Bridge, 6 isolated first-party
 conformance, 85 Widget SDK, 12 API-compatibility, and 53 documentation cases;
 the native 10,000-demand cache fixture passes and the Release OverlayHost
 target compiles.
 DLV-054 focused evidence adds cancellation-ignoring provider stall, concurrent
 list/lifecycle progress, bounded Stop, exact late-generation suppression, and
 same-AppId/SavedId/stable-identity artwork-revision rotation. Release results
 pass Windows app-library 32/32, broker 51/51, Games & Apps 56/56, Widget SDK
 85/85, production Bridge 70/70, documentation over 54 Markdown files, native
 RemoteImageCache and bridge-parser cases, 4,777 renderer checks, the production
 OverlayHost target, and the installed first-party package seam 6/6. The
 correction is private bridge/cache behavior and does not close the broader
 synchronous startup issue.
 DLV-094 extends that accepted lazy route to installed Steam registrations. The
 source accepts only direct `_icon.png`/`_icon.jpg`/`_icon.jpeg` files beneath
 the exact non-reparse trusted Steam cache root, records host-only file identity,
 length, file-change, and last-write evidence, caps input at 1 MiB / 4,096 per
 dimension / 16,777,216 decoded pixels, normalizes to the existing 64-pixel /
 12-KiB PNG contract, and retains at most 64 decoded entries. DLV-096 corrects
 the rejected candidate by retaining only bounded trusted-root/app identity
 during catalog enumeration: multi-item list and refresh perform zero artwork
 filesystem probes, while exact demand owns candidate selection, metadata,
 bytes, decode, and revalidation. A stable initial lazy generation prevents an
 unchanged neighbor from rotating merely because its first demand learned file
 evidence; replacement/removal instead fails the stale demand and rotates only
 the affected handle on refresh. Steam artwork uses a separate four-operation
 provider lane. DLV-098 makes terminal ownership exact across all three admitted
 lanes: after cancellation, one deadline drains the four artwork permits plus
 the scan and running-observation gates before any source disposal or catalog
 state clear. A timeout in any lane produces the same shared terminal failure,
 retains state, and performs no source disposal. Current Steam catalog ownership
 retains at most 4,096 locator generations; removal retires an old locator
 object, unchanged current registrations retain learned revisions, and an old
 admitted handle cannot be reinitialized by later catalog churn. Focused
 DLV-096 Release evidence passes the Windows app-library provider (56/56) and
 PlatformBroker (54/54) suites plus the exact installed generic-AppContainer
 Steam-artwork route. DLV-098 focused Release evidence passes the expanded
 Windows app-library provider suite (60/60), including cooperative and timed-out
 scan, observation, and artwork ordering plus over-bound locator churn and
 paused-decode retirement. Documentation validation covers 55 Markdown files.
 DLV-098 did not repeat the unchanged broker or installed routes, and neither
 milestone ran an aggregate, native, screenshot, or live Steam verification.
 DLV-099 couples locator ownership to the existing normalized source-generation
 commit: Steam enumeration stages a bounded candidate map without mutating the
 current map, and only the latest accepted source generation promotes that map
 and retires removed locators. Canceled and losing candidates publish nothing;
 unchanged locators retain object and learned-revision identity. Focused Release
 evidence passes the expanded Windows app-library provider suite (62/62) and
 documentation validation across 55 Markdown files. The unchanged broker,
 installed, widget, aggregate, native, and screenshot routes were not repeated.
 Deterministic managed fixtures traverse both 2,000- and 10,000-item
 providers while retaining at most 200 items and serializing at most 203 nodes.
 Focused Release evidence covers 85 WidgetSdk cases, 12 API-compatibility cases,
 49 native focus checks, 4,777 native renderer checks, and the native bridge
 parser; the clean exact-commit aggregate remains the named integration
 checkpoint rather than evidence inherited from this dirty worktree.
 The final composed production-host fixture now drives bounded managed-format
 List and responsive-Grid frames through the production bridge parser, real
 Direct2D renderer, keyed anchor reconciliation, focus-edge pagination,
 accessibility adapter, HWND provider, and UIA client. Its default 271 checks
 (274 with retained evidence-manifest validation) cover a
 fixed header, forward/reverse boundaries, eviction, insertion/deletion churn,
 empty/sparse/partial-final/last-good-error states, and eight-item host/UIA
 windows over 2,000- and 10,000-item logical collections. Focused Release
 evidence also retains 85/85 Widget SDK cases, 49 native focus checks, and
 4,777 native renderer checks; the earlier single clean aggregate remains the
 only Tier-3 run.
 DLV-060 adds a separately bundled Game Launcher while preserving Games & Apps
 as the curated tray. The launcher queries only installed trusted Game rows,
 traverses 2,000- and 10,000-item fake catalogs in 64-item provider pages,
 retains at most 192 rows and 256 cursor tokens, and renders only its retained
 responsive-grid window. Its bounded private state stores at most 96 sanitized
 SavedId/display/source rows: those warm rows are disabled and cannot authorize
 launch. A tile launches only after resolving its SavedId to a current exact
 AppId. The generic-worker fixture uses the same broker/cache/launch authority
 as Games & Apps and exercises a 10,000-row backend, adjacent keyed focus, an
 opaque lazy artwork handle, and exact launch through the AppContainer route.
 Focused Release verification
 `artifacts/verification/20260811T121636Z-e6ecf7c2` passes Widget SDK 86/86,
 PlatformBroker 52/52, Windows app-library provider 43/43, Game Launcher 10/10,
 WidgetCatalog 35/35, and the documentation contract across 55 Markdown files.
 The separately bounded installed generic-worker/AppContainer suite passes 6/6.
 DLV-074 aligns the checked public API and current author documentation with the
 intentional 256-cursor same-direction bound. Focused Release verification
 `artifacts/verification/20260811T123616Z-fe1edc94` passes API compatibility
 12/12, Widget SDK 86/86, and the documentation contract across 55 Markdown
 files; refresh or direction change still resets traversal evidence.
 DLV-066 advances Game Launcher to package 0.2.0 with one current schema-v2
 organization policy. X toggles favorites, LB explicitly groups or ungroups
 selected SavedIds, and RB chooses a preferred group member. No title heuristic
 establishes identity or redirects launch: preference changes bounded ordering
 and labels while every tile retains exact fresh SavedId resolution. The schema
 retains at most 96 display rows, 32 organized identities, 16 groups, and four
 variants per group, and remains below the 64 KiB private-state limit at those
 maxima. One two-attempt CAS store reapplies only the requested delta after a
 conflict, preserving unrelated favorite order and groups. Missing sources keep
 disabled organization tombstones; exact SavedId reappearance restores the
 choice, while replacement identity remains independent. Invalid/unsupported
 pre-release state is atomically reset to empty v2 before current reconciliation.
 Focused Release verification
 `artifacts/verification/20260811T123312Z-b54b995b` passes Game Launcher 15/15
 and the documentation contract across 55 Markdown files.
 DLV-067 advances Game Launcher to package 0.3.0 and separates launch request
 acknowledgement from adapter evidence. The first-party-only broker route
 returns only Request accepted, Launcher started, Running, or Ended plus bounded
 supported-state flags; failures remain sanitized. Current Windows and Steam
 adapters prove Launcher started only. The widget projects Pending/Failed and
 exact per-SavedId results, rejects late deactivated generations, and requests
 overlay close only when evidence is stronger than acknowledgement. No process,
 window, path, command, or store identity crosses the widget boundary.
 Focused Release verification
 `artifacts/verification/20260811T125214Z-2c2150ad` passes Windows app-library
 provider 45/45 and PlatformBroker 52/52. The final affected-suite run
 `artifacts/verification/20260811T125525Z-325c9d53` passes Game Launcher 20/20
 and the documentation contract across 55 Markdown files. The smallest installed
 generic-worker route
 `artifacts/verification/20260811T125249Z-fa9ce5d8` passes 6/6, including the
 exact current Game Launcher launch path.
 DLV-075 advances Game Launcher to package 0.4.0 with provider-owned normalized
 search, exact source/favorite criteria, and closed display/source sorts over
 one immutable cursor revision. The widget retains only its bounded cursor
 window and opaque SavedIds. Protocol v15 adds one bounded TextEntry primitive;
 the native host owns keyboard/controller editing, commit/cancel, focus
 restoration, high-contrast native controls, and UI Automation, while the
 worker receives only one final `CommittedText`. Query replacement resets the
 shared cursor generation, and stale cancellation-ignoring results cannot
 publish. Focused Release evidence passes Windows app-library provider 46/46,
 PlatformBroker 52/52, Widget SDK 87/87, API compatibility 12/12, Game Launcher
 22/22, CLI replay 56/56, the documentation contract across 55 Markdown files,
 TextEntryModalTests, the native bridge parser, and the Release OverlayHost
 target. The installed generic-worker/AppContainer route passes 6/6 at
 `artifacts/verification/20260811T134904Z-ff02c207`; the clean exact-commit
 canonical verifier `artifacts/verification/20260811T140135Z-828292b9` stopped
 on the inherited YT Music package expectation (`0.2.6` expected, `0.2.7`
 actual) after the SDK and API compatibility groups passed; it was not rerun.
 DLV-086 aligns the companion handshake's `appVersion` owner with the current
 immutable `0.2.7` manifest and keeps the manifest-equality test as the drift
 guard. No package version, package bytes, runtime contract, or companion
 authentication behavior changed.
 DLV-079 replaces DLV-075's modal-loop reference retention with one immutable
 bounded text-entry request and a fresh post-modal current-authority resolution.
 Snapshot refresh, runtime or active-widget replacement, removal, hide,
 input-scope/action replacement, and disabled/busy sources all fail closed
 before the bridge action send. TextEntryModal now uses work-area-bounded DPI
 geometry and true spatial row navigation without wraparound. Focused Release
 evidence passes TextEntryModal admission/layout/UIA/controller tests, the
 existing native bridge text-entry parser, and the production OverlayHost target.
 DLV-076 advances Game Launcher to package 0.5.0 and one current private schema
 v3. The widget retains at most 32 opaque SavedIds in deterministic newest-
 accepted order, updated only by exact current Launcher-started/Running/Ended
 evidence. Acknowledgement-only, failed, stale, replaced, and canceled outcomes
 do not write history. Recent-first ordering and exact recent-only filtering
 remain non-authorizing and compose with favorites/groups; bounded CAS replay
 preserves unrelated organization and another launch's order. Missing identity,
 replacement identity, warm restart, whole-schema reset, clear-history, and the
 64 KiB state ceiling have direct deterministic coverage. Focused Release
 verification passes Game Launcher 27/27 and the documentation contract across
 55 Markdown files.
 DLV-077 advances Game Launcher to package 0.6.0 and one current private schema
 v4 with at most 32 explicitly included opaque SavedIds. The Add games nested
 route queries only the existing trusted installed catalog, exposes current
 Game/Application/Unknown classification, and stores no executable, command,
 AUMID, store/provider identity, or launch authority. Manual rows are freshly
 resolved into the bounded Library window after restart and still require exact
 SavedId revalidation before launch. Add/remove CAS replay preserves unrelated
 favorites, groups, and recent ordering; missing and replacement identities
 remain non-authorizing. Focused Release evidence covers Game Launcher 32/32,
 Widget SDK app-library contracts, PlatformBroker exact resolution/launch,
 generic-worker routing, an installed add/exact-launch route, direct restart,
 remove, stale-rejection fixtures, and 55 documentation contracts.
 DLV-080 corrects the retained-slice composition without changing schema v4 or
 provider contracts. Every cursor loader result now contains only the provider
 page (at most 64 rows); up to 32 freshly resolved recent and 32 manually added
 rows render in separate non-anchor sections, deduplicated by exact SavedId
 before the provider grid. Recent: First therefore leads with a later-page game
 after cold restart, while launch still performs fresh exact resolution.
 Automatic Game registrations are disabled and labeled Included in Add games,
 and obsolete manual Game membership is removed through bounded CAS state
 mutation. Focused Release coverage includes a disjoint 64 + 32 + 32 semantic
 tree, source/favorite/sort coexistence, traversal overlap deduplication,
 missing/replacement identities, and the installed 64-row-plus-manual route.
 DLV-082 carries the normalized provider's exact source health and source
 revision through one additive app-library page contract. The broker accepts at
 most 16 unique opaque source IDs with bounded labels and safe status codes;
 the SDK rejects malformed or over-bound responses and grants no per-source
 operation. Game Launcher renders Healthy, Degraded, Unavailable, and transient
 Refreshing text while retaining usable games and the existing refresh owner.
 Deterministic source-isolation, partial-result, stale/recovery/disappearance,
 broker validation, SDK compatibility, installed-worker, and documentation
 evidence accompanies the public API baseline addition.
 DLV-083 scopes LB/RB page switching to the Game Launcher results scroll and
 composes it with the existing cursor owner. Only current Ready directions are
 authored, so boundary, busy, repeated, and replaced input cannot create an
 additional page transition; fixed sections remain outside cursor accounting.
 Single-page tiles retain their existing variant-group and preference shortcuts.
 DLV-084 advances Game Launcher private organization to schema v5 and adds a
 bounded exact-SavedId hide/restore policy. Y hides only a current resolved game;
 the Hidden route retains at most 32 sanitized display rows and never carries
 launch authority. Restore removes one exclusion while bounded CAS replay
 preserves unrelated favorites, groups, recent order, and manual membership.
 Missing rows remain display-only, same-title replacement identities stay
 independent, and incompatible development schemas reset as a whole. The
 128-row/96-character display projection remains below the 64 KiB private-state
 limit at its proven worst case. Focused Release coverage passes Game Launcher
 42/42 and the documentation contract across 55 Markdown files. The retained
 installed-worker run at
 `artifacts/verification/20260811T170031Z-c45cff26` passes 5/6: after the new
 Hide/Restore route returns to Library, the fixture still observes the saved
 rows as unavailable after the visible reload control. That installed
 transition remains explicit verification debt for planner disposition; it is
 not reported as a passing AppContainer acceptance result.
 DLV-095 adds a separate optional `system.apps.running.read.v1` grant and an
 explicit **Add running app** route to Games & Apps and Game Launcher. The
 trusted Windows provider observes visible unowned top-level application windows
 on demand, excludes owned/cloaked/background/inaccessible/elevated/overlay/
 worker/tool windows, and maps packaged or canonical executable identity
 one-to-one to a current normalized registration. DLV-097 bounds native work
 before eligibility filtering: at most 256 top-level callbacks and therefore at
 most 256 process-open attempts occur per observation, while duplicate collapse
 and the lower public result cap remain 64. Broker and SDK payloads contain only
 a sanitized name, closed kind/source, authority-scoped SavedId, and short-lived
 revision; PID, HWND, path, command, AUMID, package identity, and provider
 evidence remain host-only. Add confirms the current observation and
 registration before reusing each widget's existing bounded SavedId CAS policy.
 The SDK now applies the same closed AppId/SavedId/kind/display/source validation
 used by pages and resolution to every non-null confirmation response; malformed
 data returns `malformed_response` before either widget can project or persist
 it. Focused Release evidence passes Widget SDK 87/87, API compatibility 12/12,
 Windows app-library provider 57/57, Games & Apps 59/59, and Game Launcher 45/45.
 The prior credential-free generic-AppContainer route remains the packaged
 DLV-095 evidence; no aggregate or screenshot verification ran for DLV-097.
 DLV-007 now captures Spotify rendering through one private immutable
 presentation revision and keys playlist detail by playlist ID plus selection
 generation. Forced Release interleavings cover Back, rapid reselection, late
 success and failure, refresh, and Active-lifetime cancellation/reactivation.
 DLV-008 keeps that one widget/resource owner while separating lifecycle and
 action wiring, route data, playback behavior, and snapshot-only presentation
 into named internal partials; a source-boundary contract prevents duplicated
 coordination or mutable reads in view composition. DLV-023 adds one typed
 refresh-failure policy shared by Y/manual refresh and Active polling. Transient
 provider, malformed-response, and unexpected failures retain the last accepted
 Ready playback, route, collection state, and focus behind one safe diagnostic
 warning; automatic retry saturates through 5/15/30-second delays. Permission,
 authorization, and compatibility faults still clear provider-derived data and
 select their explicit safe states. Forced Release coverage passes Spotify
 39/39, Widget SDK 84/84, and the installed generic AppContainer route 6/6,
 including broker-enforced playback-read revocation. Retained SDK and installed
 worker results are `20260810T110052Z-75e0b660` and
 `20260810T110105Z-7025cb34`.
 Packaged physical-controller evidence and a live retest remain open. The
 controller guide is
 density-aware and no-wrap, the widget viewport has a
 host-owned rounded clip, and size-changing widget swaps commit one synchronous
 complete repaint after a no-redraw move to avoid an intermediate black frame.
 These
changes remain in Verifying until packaged controller and screenshot evidence
is recorded in GBA-015 and GBA-016.

### Settings and global theme pipeline

The first-party Settings widget controls text/interface scale, backdrop
opacity, System/Full/Reduced motion, System/Standard/High contrast, bold text,
reduced transparency, exact theme ID/version selection, confirmed inactive
user-theme version removal, and confirmed reset. Theme versions are grouped by
ID; invalid versions remain reviewable/removable, while built-in and selected
versions are protected.
`PlatformAppearanceService` watches settings and theme files
with event notifications plus a 200 ms debounce—there is no polling loop.
Valid reloads increment an immutable revision, clear per-widget layered-theme
caches, and emit a bridge appearance-change event. Invalid reloads keep the
prior snapshot and revision.

The bridge resolves platform → widget → user layers, with layer priority
stronger than selector specificity. It publishes globally layered widget
`base`/`focused` styles and a bounded shell appearance containing scale,
backdrop, motion, contrast, bold-text, transparency, and semantic shell styles
without launching widget workers.
The native client consumes the initial shell appearance and live revision
events, rejects stale revisions, retains its last good state on failure, and
applies supported shell styles and the complete bounded appearance record. The
host-owned policy runs after every shell/widget GBSS layer: text scale
remeasures/reflows without compounding inherited `em`, reduced motion removes
transitions, reduced transparency removes blur and makes node surfaces opaque,
bold text enforces minimum weight 600, and System/forced high contrast corrects
text/focus against inherited surfaces with a geometric focus ring. Windows
setting changes reapply System contrast and motion immediately.

The built-in default and first-party styles now form one minimalist warm-
graphite baseline with regular-weight hierarchy, fewer nested surfaces,
smaller radii, thin borders/tracks, compact controller targets, and one inset
neutral focus cue. Stable declarative nodes interpolate bounded opacity, scale,
and `translate-x`/`translate-y` targets through a host-owned timeline; first
observation snaps, rapid changes retarget from the presented value, reduced
motion cancels, removed widgets are forgotten, and settled content schedules
no further frames. Translation is true subtree presentation geometry: paint,
clipping, focus, hit testing, controller navigation, and focus-follow scrolling
share the translated boxes while static layout size does not change. The
transient pressed-state map is connected to exact physical actions. Shell/
widget open, close, and replacement transitions plus packaged visual/
accessibility evidence remain open. The embedded selectable
Cool Slate theme exercises the same token and renderer pipeline with a visibly
distinct palette rather than a hard-coded widget skin.

The CLI provides `gbar theme new|validate|preview|pack|inspect|install|list|remove`.
Schema-version-2 `.gbartheme` packages are data-only, deterministic, bounded,
publisher-namespaced, digest-addressable, revalidated through the production
compiler, and installed as immutable ID/version directories through staged
atomic moves. Remote HTTPS/GitHub release installs require a pinned SHA-256.
Settings and CLI removal share one exact-version catalog mutation policy and
cross-process lock; atomic retirement never rewrites the appearance record or
sibling versions. Preview is computed terminal output, not native pixels;
signing, revocation, automatic update/rollback, asset support, and graphical
preview remain open.

Focused DLV-111 Release evidence covers PlatformSettings theme mutation
(16/16), controller Settings version review/selection/confirmed removal
(55/55), CLI parity (58/58), and documentation contracts. The bounded cases
include multiple IDs/versions, invalid exact identity, built-in/current
protection, cancellation before commit, sibling preservation, deterministic
post-removal focus, and the existing no-poll watcher path.

### Live package catalog

`BridgeCatalogMonitor` watches the packaged host catalog (trusted `widgets` plus
manifest-backed `bundledWidgets`) and the
current-user `catalog-state.json` and packages subtree. A capacity-one channel
coalesces file hints and debounces write bursts for 175 ms before a complete
bounded reload. Staging, cross-process lock, and atomic temporary files are
ignored. Semantic changes advance a bridge-lifetime revision; invalid trusted
shell state retains the complete last-good catalog/revision. Invalid installed
state or package integrity instead publishes a trusted-only revision,
synchronously removes Community registrations, and retires their workers.
Invalid individual styles or unsupported capabilities are isolated with
bounded diagnostics. Reload/list never starts a worker.
Starting the monitor schedules a complete catch-up reload after both watchers
are active, closing the initial load-to-watch race.

Catalog-state schema 2 adds an optional exact active-version pin while retaining
schema-1 reads. Without a pin, discovery selects the greatest installed
`System.Version`; the next successful mutation migrates legacy state to schema
2. `gbar version list|select|rollback` exposes immutable installed versions.
Selection and rollback require a disabled widget, keep it disabled for review,
and never rewrite package bytes. A missing pinned directory fails discovery
closed with `active_version_missing` rather than silently executing another
version.

`gbar uninstall <widget-id>` is implemented as a disabled-only catalog
operation. It removes the exact ID's state, atomically retires its complete
package directory from discovery, deletes all immutable versions, and
reindexes remaining order. Locked retired files are reported honestly and a
later package mutation retries bounded staging cleanup. It intentionally does
not guess or enumerate
provider-owned private-secret slots; cleanup of known slots remains a widget
host-service responsibility.

Quota failure no longer removes the cleanup control plane. `WidgetCatalog`
publishes a separately bounded health projection from canonical ID/version
directory names plus validated state, without parsing candidate manifests or
loading code. It identifies ID, per-widget-version, and total-version quota
breaches and marks the selected generation as protected, including when it is
enabled. Settings presents only inactive, non-selected candidates behind an
exact confirmation page;
`gbar repair list|remove` provides the same workflow. Exact-version removal
runs under the catalog operation lock, rejects reparse/path ambiguity, checks
cancellation before the atomic staging move, and never exposes a force or
caller-selected recursive deletion path. Normal discovery is re-run after
repair and remains the only publication/launch authority.
The bounded Release regression projects the maximum supported 512-version
repair view in 326.382 ms with 1,711,440 managed bytes allocated on the current
development machine (5 s and 32 MiB test budgets).

Presentation/order-only changes preserve compatible workers while atomically
swapping their validated presentation/quick-action metadata. A package,
publisher, instance, executable, argument, declared-capability, or memory-policy
change retires the prior client; stale events are ignored and the next use
starts/authenticates the replacement lazily. The native host coalesces catalog
events, holds a revision in-flight until an atomic descriptor list parses and
reconciles, and retries a genuine in-flight change event at bounded 250, 500,
and 1000 ms delays. Hiding the overlay cancels and abandons that retry sequence;
the same revision may be announced again, and a later open/list catches up.
Ordinary open-time failures do not create polling. A new bridge session resets
tracking. Reconciliation invalidates affected snapshot/style caches, clears
every runtime-changed widget's focus/lifecycle independently of cache
residency, and safely returns from a disabled/removed active widget.

### Worker lifecycle

The implemented host-authoritative lifecycle separates presentation from
residency:

- **Created:** runtime initialization and the one-time author hook.
- **Background:** resident by default but not selected/open. Visible-lifetime
  work is canceled; explicitly permitted widget-lifetime background work may
  continue.
- **Visible:** the dashboard card is selected and can receive declared quick
  actions.
- **Interactive:** the widget is open and receives its scoped controller
  actions.
- **Destroying:** terminal bounded cleanup after widget-owned tokens are
  canceled.

The managed SDK exposes widget-lifetime, per-state, and shared
Visible/Interactive lifetime tokens, plus creation, stable-state-change, and
destroying hooks. The native host publishes `Visible` for the selected bridge
card, `Interactive` for its open surface, and `Background` when hidden or
switched. Authors cannot request their own lifecycle transitions.

Manifest `residencyPolicy` schema 1 is enforced generically for trusted and
installed workers. `keep-alive` is the default; `suspend-when-hidden` keeps the
process but suppresses hidden presentation/interaction and uses cooperative
`Background` cancellation; `unload-after-idle` requires 5–86,400 seconds,
caches the last validated snapshot, sends bounded `Destroying`, releases the
process/companion, and lazily resumes on visibility. Pending unload cancels on
visibility or work, and intentional unload does not consume crash budget.
Legacy `backgroundPolicy: none|suspend` resolves deterministically to
keep-alive/suspend-when-hidden; declaring both vocabularies fails validation.
No policy suspends Windows threads. The capability broker continues to deny all
new ordinary manifest-declared operations/subscriptions while Background. One
narrow OAuth exception allows only an already-started
`external.spotify.authorization.v1` `connect` request to retain its lease when
an explicit Interactive Connect action opens the browser and moves the widget
through Visible/Background. The action acknowledges immediately; its
authorization task uses the widget's Created-to-Destroying lifetime rather than
the input-action or active-surface token. It does not authorize a new inactive
request or a disconnect; Destroying, consent revocation, pipe/caller
cancellation, and the provider's bounded timeout still terminate it. The
callback listener exists only for that explicit attempt. The host-granted
bounded private-state read/write/clear service
remains available
there for persistence and is denied during Destroying.
Separately, trusted bridge policy assigns each Windows worker process tree to
one accounting Job before resume and retains kill-on-close regardless of
lifecycle. It does not impose an arbitrary memory or one-process ceiling.
Crash, hang, shutdown, and user-requested termination remain separate safety/
administrative paths.

### Current native performance baselines

DLV-016 combines the existing production-host process sampler with one focused
native semantic-churn target. Current bounded host run
`overlay-performance-20260811-063511359-b40af482` records 21 observations per
state: Hidden CPU p95 is **0.09737%**, working set p95 **102.5 MiB**, private
bytes p95 **49.8 MiB**, three processes, zero host timer messages, and zero
post-warmup Direct2D frames. Visible-idle CPU p95 is **0.09773%**, working set
p95 **219.4 MiB**, private bytes p95 **132.4 MiB**, five processes, 34.12584
host timer messages per second, and zero post-warmup Direct2D frames. The
bounded provider still cannot supply process-tree private working set, so that
target remains explicitly unavailable rather than being replaced by another
memory metric.

Repeated native run `dlv016-native-20260811T063924Z-2b5ac019` uses five fresh
Release processes over a stable 55-node snapshot and 48-node semantic tree.
Each process performs 256 updates; input-to-projection p95 ranges from
**0.399–0.591 ms**, hidden CPU from **0–0.1285%**, visible-idle CPU is **0%**
at this process-time resolution, and private working set ranges from
**0.63 MiB** hidden through at most **1.32 MiB** after input updates.
Each update keeps changed semantics within a four-node neighborhood; every run
projects 12,288 nodes and retains the same 19,588-byte canonical snapshot.
The 50 ms response and 1 MiB protocol bounds are existing contracts; 5% idle
CPU and 128 MiB private working set are broad material-regression ceilings, not
new product budgets.

`scripts\Measure-OverlayPerformance.ps1` writes schema-2 production-host
JSON/Markdown with machine/build metadata, process CPU/memory/handle/thread
samples, readiness proxies, native counters, and non-gating comparisons.
`scripts\Measure-NativeSemanticChurn.ps1` builds only the new canonical target,
runs 3–10 deadline-bound samples, separates QPC/empty-phase overhead, and writes
immutable sample plus aggregate evidence with exact source/executable hashes.
Null-target semantics do not measure paint, GPU, DWM, controller hardware, or
game-frame cost; neither harness measures scheduler wakeups or long-run/many-
widget trends. Those limits remain explicit and the dirty runs are focused
implementation evidence, not release or marketing proof.

DLV-011 selected a bounded host-owned Win32 tool-window architecture. DLV-058
now supplies the first generic declarative product lifecycle. The strict
optional public-manifest `pinningSupported` boolean defaults false; the bridge
projects it as data, while `WidgetSurfaceCoordinator` alone owns exact catalog
and package generations, the real HWND, native declarative rendering,
topmost/focus/input policy, host chrome/status semantics, one-surface admission,
live snapshot updates, and exact teardown. `PinnedSurfacePolicy` retains the
closed click-through/focusable modes and work-area-contained default placement.
The real-HWND fixtures verify the window-style contract, nonactivating hit
testing, explicit focusable transition, UI Automation ownership, survival after
the main overlay hides, and paired HWND/semantic teardown. A widget cannot
provide an HWND, renderer, provider, process, compositor, or z-order handle.
New pins begin click-through; explicit `P` toggles interaction, `U` unpins, and
removal, generation replacement, worker restart/loss, close, or host exit clean
up exactly once. The focused DLV-058 fixture passed 33 checks with a
10,264,576-byte incremental private-working-set observation, below DLV-016's
material gate.

DLV-070 moves process ownership ahead of every `OverlayApp` initialization.
One race-safe global per-user/profile mutex elects the owner; later ordinary or
`--show` launches become bounded clients of a user-only, remote-rejecting named
pipe. The owner validates the connected token SID before accepting the closed
version-1 Show frame. A client receives one acknowledgement and exits before
bridge, catalog, controller, or HWND initialization. The transport queues Show
even while the owner has no HWND, binds it when initialization completes, and
releases the mutex, pipe thread, events, and notification target exactly once.
Abandoned mutex ownership recovers after a crash; a claimed endpoint, malformed
client, wrong profile, or missing acknowledgement fails closed without killing
another process. The focused Release fixture passes 20 hidden/visible,
simultaneous, malformed, squatted-endpoint, timeout, abandoned-owner, and
orderly-exit checks. The isolated exact-executable probe confirms the no-HWND
owner receives Show and both processes exit 0 without initializing `OverlayApp`
in the probe owner or client.

DLV-068 adds one generation-bound `PlacementSession` and schema-1 durable
geometry store to the same coordinator. Placement records contain only widget
ID, stable monitor ID, normalized work-area anchors, and logical DIP size;
atomic replacement is bounded to 64 records and malformed/incompatible state
resets closed. Controller Menu/View, keyboard M/R, host pointer chrome, and UIA
Move/Resize all enter the same Move/Resize preview. Directional input changes a
work-area-constrained rectangle, A/Enter commits, and B/Escape/capture loss or
overlay close restores the exact original rectangle. Commit revalidates runtime
and presentation generations before writing. DPI, work-area, orientation,
topology, and monitor loss resolve the last logical placement through one
normalized-anchor policy; a work area below the host-injected minimum fails
closed. The focused Release evidence passes 16 pure placement/persistence
checks and 41 real-HWND lifecycle/placement/UIA checks. The latter observed a
10,833,920-byte incremental private-working-set delta, below DLV-016's material
gate. Placement adds no hidden/idle timer.

DLV-069 completes generic pin input and accessibility composition in the same
coordinator. Interactive pins own one explicit controller focus entered by
right-stick click; shared authored/geometric navigation moves it, A uses one
bounded generation/snapshot/scope queue, and B/right-stick returns to the
overlay and restores Click-through. Guide closes through the existing global
authority, X closes the focused pin, and LB+RB+X or Ctrl+Shift+H performs one
host-owned emergency unpin. Pointer capture loss cancels activation. Interactive
UIA composes current widget semantics after bounded host Enter/Exit, Move,
Resize, Click-through, Unpin, Close, and Emergency actions; Click-through omits
all interactive descendants. Safe failures are assertive live status, Windows
high contrast supplies system colors, and reduced motion remains immediate.
Focused Release evidence passes 306 policy/real-HWND checks and 76 production-
coordinator checks, including actual coordinator monitor-loss reconciliation,
focus-valid on-screen recovery, and Move/Resize/Commit/Cancel bounds inside the
minimum surface. The latter observed an 11,026,432-byte incremental private-
working-set delta below the DLV-016 gate, with no hidden/idle timer added.

DLV-073 corrects the rejected Click-through paint branch without changing
interaction authority: the coordinator no longer covers the admitted widget
viewport with an opaque placeholder after rendering it. The existing renderer
continues to present current snapshots while Click-through still exits focus,
rejects hit testing and input, empties pending requests, and publishes only
nonactionable host semantics. The 308-check pure host-policy run and 80-check
focused real-HWND run include a
stateless production-paint trace proving sentinel sequence 1 survives the mode
and hidden-overlay sequence 3 repaints before reopen; its incremental private-
working-set delta is 11,370,496 bytes. The coordinator remains 1,416 aggregate
physical lines before and after (1,203 + 213 versus 1,188 + 228), with the same
single admission/HWND/render/focus/input/placement/UIA/teardown owner and no new
mutable dependency, state machine, timer, public API, media authority, or
compositor.

Five-process focused Release evidence `dlv011-native-20260811T070422Z` passes
302 checks per retained sample. Incremental private working set is
**0.684-0.707 MiB**, normalized 750 ms idle CPU is **0%**, and the two-node host
semantic projection p95 is **0.0003-0.0005 ms** over 256 updates. These are
bounded increments against DLV-016's material gates, not substitutes for its
larger semantic or real production-process measurements. Windows App SDK
`CompactOverlayPresenter` remains a documented comparison only: adopting its
new runtime/deployment dependency crosses the assignment's architecture stop
condition. Physical borderless-game, mixed-display/hot-plug, HDR, exclusive-
fullscreen, GPU/DWM, and hardware-input claims remain unverified. See
[host-owned pinned-surface feasibility](pinned-surfaces.md).

### Test coverage

The repository verification script builds and runs managed suites for the SDK,
protocol, YT Music, first-party Settings, runtime, CLI, styling, platform
settings/themes, catalog, bridge, the generic worker host, broker, the real
first-party-package conformance path, and Windows providers/reference widgets.
The runtime covers suspended pre-containment
launch, memory/process/UI limits, kill-on-close, restart cleanup, and mandatory
community AppContainer authority, exact grant replacement, content-generation
isolation, trusted-runtime/content-root overlap refusal, caller-preserving
content-admission cancellation, timeouts and session release, bounded intentional
unload, and private two-clock dashboard-gesture propagation. Its focused Release
harness passes 74/74, including direct session, pending-request, gesture-
reservation, construction-stop, stale-response/publication, and late-grant revocation
fixtures. The authority fixtures prove reverse restoration of every
attempted root/directory/file DACL including the failing target, retry without
quarantine after a complete rollback, pre-mutation refusal when journal
publication fails, cross-profile lock ownership, corrupt/hostile-entry refusal,
and recovery after an incomplete rollback. Profile-keyed records let a disjoint
generation proceed while equal, nested, or same-object targets remain blocked;
legacy global records recover with their recorded profile owner. The host-only
recovery service lists only confirmation token/profile/count metadata, clears
only after exact verified restoration, and rejects stale confirmation. A
bounded child process terminates
immediately after the second real DACL mutation; the next host restores and
exactly verifies all original DACLs from the pending record before clearing it.
The production AppContainer probe also proves the sandbox cannot read or write
the protected host journal. Additional fixtures rename and replace a captured
file, prove handle-bound apply changes only the original object, reject a
different volume/file identity during fresh-host recovery without touching the
replacement or clearing the journal, and refuse any different AppContainer
package SID authority before mutation. All failure paths reject before process
launch and release the content lease. New cases reject missing, malformed, duplicate, and
byte-identical replacement identity evidence before authority capture or journal
publication, while releasing both residency and content leases. The bounded
512-file exact-grant fixture completes in 471.257 ms in the current focused Release run;
the isolation probe verifies distinct stable SIDs, Low integrity, zero
capability SIDs, allowed package reads, denied package writes/host and other-
profile reads/network, stripped secrets, private-profile write/isolation, and
bounded cleanup. Canonical all-lane Release verification
`20260810T005210Z-c46e6881` passed 41/41 steps in 599.821 seconds at stable
start/finish commit `dc30be9`, including 58/58 runtime tests, the native build
and tests, documentation contracts, and the hidden-overlay smoke test. The run
recorded 29 artifact digests; it is intentionally not clean-tree release
evidence because the milestone and unrelated review work were present at both
 endpoints. The current SDK, SDK Gallery, and YT Music focused suites pass
 84/84, 6/6, and 55/55 respectively, including navigation/resource contracts,
 safe typed companion errors, serialization, and widget recovery for host-side
 rejected-Bearer invalidation without a second widget delete. YT Music also
 proves late pairing and polling cannot publish after deactivation, direct
 confirmation-window expiry and exact-feature rollback, closed action and
 connection transitions, byte-deterministic repeated snapshot composition,
 and that no widget-owned Task or CancellationTokenSource registry remains.
DLV-036 reduces the logical Settings partial type from 2,872 lines across its
1,090-line root, 794-line installed-widget section, and 988-line permissions
section to 2,324 lines across a 542-line root and the unchanged residual
sections. The moved behavior is owned by non-partial snapshot presentation,
closed navigation/preference policy, and exact-token authority-recovery policy
boundaries rather than another partial-file split. One lifecycle, operation
gate, committed-state lock, service set, and invalidation owner remain in the
root. Direct fixtures prove repeatable semantic rendering, bounded ordinary
preference mutation and persistence, rejection of privileged actions by the
ordinary policy, exact reviewed-token replacement refusal, closed recovery
results, and token-free snapshots. The current Settings Release suite passes
49/49, including
scrollable identity and permission review,
disabled-only version selection/rollback, required/optional separation,
enablement-versus-consent copy, fail-closed catalog/compatibility behavior,
nested visual-accessibility controls, legacy appearance defaults, and no
polling. Focused dirty-worktree run
`20260810T192328Z-f5898174` passes Settings 49/49, PlatformDiagnostics
15/15, Widget SDK 84/84 after a clean SDK build, and the documentation
contract across 52 Markdown files in 20.5 seconds; it is scoped milestone
evidence, not a clean aggregate.

DLV-044 completes the residual partial-type disposition. The DLV-036 boundary
started with one 2,324-line logical widget and 44 service, coordination, and
committed-state fields across a 542-line root plus 794/988-line installed and
permission partials. It now has one non-partial 1,133-line effect adapter with
20 fields, including exactly one operation gate, state lock, installed state,
permission state, lifecycle, and invalidation authority. Installed rules and
view composition are separate 221/420-line value policy and snapshot
presenter; permission/consent rules and composition are separate 344/526-line
policy and presenter. The presenters and policies add no service reference,
lock, task, cancellation source, lifecycle, or invalidation path. Their only
mutable dependency is the immutable value supplied by the root, and every
transition returns a replacement value for that root to commit. Direct tests
cover selection removal, authority replacement, consent revocation, catalog
failure, busy presentation, repeated rendering, and canceled refresh, while
the composed suite retains exact install/version/repair, permission safe-copy,
focus, and recovery behavior. The remaining greater-than-1,000-line root is a
cohesive exception: it is the single adapter for settings/theme/catalog/
consent/diagnostics effects, action serialization, committed-state mutation,
and invalidation; it contains no section view composition or duplicated
selection state machine.

Focused dirty-worktree run `20260810T195050Z-0c20315d` passes Settings
53/53, Widget Catalog 35/35, Platform Broker 51/51, Widget SDK 84/84 after a
clean SDK build, and the documentation contract across 52 Markdown files in
32.7 seconds. It is scoped DLV-044 evidence, not a clean aggregate.

Styling and platform settings/themes pass
23/23 and 15/15,
including Busy-state composition and legacy schema-1 theme compatibility. CLI
passes 53/53, including an unrelated-directory offline scaffold that builds
against its content-addressed local SDK package, drives generated
lifecycle/state/action snapshot coverage, validates/renders/replays, produces
byte-identical symbol-free packages without checkout paths, installs two
versions, selects/rolls back, and removes them without hand edits. It also
proves actionable missing-entrypoint refusal, bounded data-only snapshot rendering, fail-closed DLL
and scenario execution, bounded scenario-manifest discovery,
authenticated candidate/active development readiness,
last-good retention/restart, complete bounded source/package watching, cleanup
failure reporting, version
list/selection/rollback, exact-stream local/remote update policy, theme
scaffold, production validation/computed preview, deterministic
packaging/inspection, pinned-GitHub installation, per-package and aggregate
catalog limits, data-only exact-version recovery, immutable versions, rejection of a 1,025-directory package
before pack output or installed-byte publication, and adversarial package cases. Catalog
passes 35/35, including
schema-1 state migration, exact active-version pins and enabled-history repair,
linearizable concurrent rollback/first-install operations, public-API
disabled-update enforcement, lock-free reads during atomic state replacement,
pin-preserving reorder, shared host-API/architecture evaluation, and exact-
content-tree sealing/tamper rejection, consumed-byte manifest/integrity limits,
misreported and changing-length rejection, verified manifest-byte capture,
session byte pinning, pre-start mutation and late-insertion refusal,
prospective ID/version/file/byte refusal, per-entry/per-64-KiB cancellation and
deadline checks, pre-extraction refusal when implicit file paths would require
more than 1,024 exact authority directories, pre-walk launch refusal for 1,025
verified files, unexpected-entry rejection, plus
content-bound unsigned authority. The same focused run proves the catalog's
typed target identities cover every pinned directory/file, block file and root
renames during the lease, and release those locks deterministically.
The maximum-inventory fixtures retain 512 verified files: current local Release
evidence acquires and hashes the pinned-file lease in 350.128 ms and applies the
exact AppContainer grants plus lazy activation in 360.426 ms. Clean eligible
run `20260809T152831Z-67b77c73` retains the seven affected Release steps for
commit `6bd60d3` (implementation baseline `6fc9e01`). Focused tests use
separate five-second content-acquisition and ten-second maximum-grant ceilings;
one aggregate production start deadline plus repeated clean/hosted measurements
remain open.
The exact accepted namespace edge is also exercised through the public
pack/install/enable path and a real first-party AppContainer worker: 1,024
authority directories and 258 verified files pack in 376.140 ms and reach a
first validated render in 2,528.883 ms in the retained clean measurement. The
current focused Release run packs in 348.583 ms and activates in 2,647.495 ms.
Clean retained
selected run `20260809T155221Z-ae6e5d8d` passed CLI 50/50, Documentation 1/1,
and First-Party Conformance 6/6 for documentation commit `b2956ab` over
implementation `4f903b0`. This is a focused one-machine measurement, not a
production startup budget or a complete all-manifest verification run.
Bridge
passes 46/46, including bounded 16-request correlated dispatch, stable
`bridge_busy` saturation, cooperative stalled-admission list/Stop
responsiveness and cleanup, duplicate-active-ID refusal, per-widget receive
ordering, aggregate count/declared-memory admission, exact crash/
timeout/idle-unload release, Settings control-plane access, race-safe refusal,
semantic catalog revisions/last-good/catch-up reload,
atomic presentation metadata replacement, compatible-worker reconciliation,
trusted Job-only exceptions, manifest-backed bundled packages, mandatory
installed-package isolation metadata, lifecycle residency, and exact dashboard
gesture derivation, including trusted-only publication and worker retirement
when installed state/integrity fails, pre-launch content-race rejection, and
live-byte pinning until asynchronous worker teardown completes. Its installed
lease now transports one typed path/role/identity sequence and proves consistent
object identity for package-root, traversal-directory, and file roles. Clean retained
selected run `20260809T161934Z-5d0bee6a` passes Bridge 45/45, Documentation 1/1,
and First-Party Conformance 6/6 in 75.643 seconds for commit `fdcf253`; its
provenance records a clean tree, release eligibility, and zero stderr or output
truncation. The dispatcher does not hard-bound cancellation-ignoring Windows ACL
calls or its final drain, and the shipping native client cannot pipeline while
its synchronous read blocks the UI; those are still release-blocking startup/
availability work.

Clean release-eligible all-lane run `20260809T171327Z-7e90ff88` passes 41/41
steps in 323.958 seconds for action-admission documentation commit `689a933`
over implementation commits `6c5f932`, `7d33ce1`, and `7d92dcd`; it records a
clean source tree, zero stderr, and no output truncation. The subsequent native
per-widget failure-feedback slice now passes its 305-check deterministic target,
including the host pump/surface/timer/catalog/hide-stop composition seam, and the
Release OverlayHost target links successfully. The prior canonical Release
native build/test script also covers the full host and existing state-machine
suites. All-lane run `20260809T225934Z-a39ac018` passes 41/41 steps in
350.377 seconds, including the 305-check target, full native build, hidden-host
smoke test, and documentation contract. Its provenance records dirty base commit
`023ea46` and `releaseEvidenceEligible: false` because this milestone and the two
reviewer-owned ledgers were uncommitted; it is integration evidence, not a clean
immutable release bundle. First-Party Conformance 6/6 now also
runs Spotify's real installed and bundled package through the generic
AppContainer worker, reaches a broker-backed ready snapshot without credentials,
admits `spotify.next` through the production action queue, and observes the
typed simulated broker command. The existing YT Music acceptance route already
proves direct and controller-resolved playback commands through its installed
generic worker; live accounts and browser authorization remain separate gates.

The verification runner now acquires one repository-scoped live file lease
before provenance and holds it through result publication. A contending process
fails with `verification_lease_busy` before writing its fixture marker; normal
release and forced holder death both permit the next process. Schema-v2 results
recapture commit and full porcelain status, recompute package digests after all
steps, report stability and bounded ineligibility reasons, and require a passed,
clean, identical start/end state. Focused wrapper run
`20260809T231421Z-73303441` passes its runner step and records identical commit
`ddb66c2`, identical dirty fingerprints, 29 final package digests,
`repositoryStateStable: true`, and `releaseEvidenceEligible: false` with exact
start/finish dirty reasons. A clean non-overlapping exact-HEAD all-lane bundle
remains pending while the reviewer-owned ledgers are intentionally dirty.
Final leased all-lane run `20260809T232451Z-15d2e7b6` passes 41/41 steps in
326.534 seconds with identical start/end commit `ddb66c2`, identical status
fingerprints, 29 final package digests, and the same two explicit dirty reasons.
While it held the lease, a second real `Verify.ps1` invocation exited in one
second before its requested step and reported the owner's PID, run ID,
configuration, and start time; the primary run completed without shared-output
contention.

Transactional content-authority all-lane run
`20260809T234957Z-6afea078` passes 41/41 steps in 367.615 seconds, including
WidgetRuntime 52/52, the full native build, hidden-host smoke test, and
documentation contract. Schema-v2 provenance records identical start/end
commit `7c8a5b8`, an unchanged dirty-status fingerprint, 29 final package
digests, and `repositoryStateStable: true`. It is integration evidence rather
than a release-eligible bundle because the milestone and reviewer-owned ledgers
were dirty at both boundaries; the retained result reports exactly
`starting_worktree_dirty` and `finished_worktree_dirty`.

Host-journal all-lane run `20260810T002639Z-3dcb61c2` passes 41/41 steps in
348.503 seconds, including WidgetRuntime 55/55, Bridge 46/46, First-Party
Conformance 6/6, the full native build, hidden-host smoke, and documentation
contract. Schema-v2 provenance records identical start/end commit `0be052b`,
an unchanged dirty-status fingerprint, 29 final package digests, and
`repositoryStateStable: true`; it is correctly ineligible because the milestone
and reviewer-owned ledgers were dirty at both boundaries. The first bounded
attempt `20260810T001859Z-b601e2d3` reached Conformance 6/6 but failed native
linking because an existing workspace `OverlayHost.exe --show` held the exact
output file. That PID was identity-checked and exited through `WM_CLOSE`; the
successful rerun left no OverlayHost process behind.

Catalog-identity handoff run `20260810T014706Z-852d0250` passes 41/41 Release
steps in 367.882 seconds, including WidgetRuntime 60/60, WidgetCatalog 35/35,
Bridge 46/46, First-Party Conformance 6/6, the full native build and tests,
hidden-host smoke, and the 49-file documentation contract. Schema-v2
provenance records identical start/end commit `0ff403a`, an unchanged dirty
status fingerprint, 29 final package digests, and
`repositoryStateStable: true`. It is integration evidence rather than a
release-eligible bundle because the milestone and reviewer-owned ledgers were
dirty at both endpoints; the exact reasons are `starting_worktree_dirty` and
`finished_worktree_dirty`.

PlatformBroker focused coverage passes
51/51 and includes direct domain routing/boundary fixtures plus closed isolated-
client SID/
pipe scopes, nonce/full-identity authentication, bounded requests/events,
consent/lifecycle gates, revocation, and cancellation of already in-flight
provider work when lifecycle or consent changes, plus dormant-reservation and
exact-operation single-use broker-lease expiry/replay/revocation checks and
dependent-lease 401 Bearer invalidation. An
actual AppContainer-to-broker
request integration also passes with the exact SID, Low-label global endpoint,
expected PID, nonce, and widget identity checks in force.
 The generic worker-host suite passes 9/9, including that real typed broker
 request from an AppContainer worker. Audio provider and Audio Mixer pass 15/15
 and 25/25; Network provider and Network Controls pass 31/31 and 17/17.
 Bluetooth provider passes 11/11; the constrained Windows Community provider
 passes 10/10, including restart/authority isolation, CAS, a real two-process
 race, quota/corruption/reparse, rate, and cancellation. Games & Apps and its
 Windows app-library provider retain focused suites covering the in-memory
 Library/Catalog flow and exact shortcut launch revalidation.
 The retained Recent Apps and Windows activity reference suites pass 8/8 and
 10/10; Windows Media provider and Now Playing pass 11/11 and 17/17. Games &
 Apps passes 26/26, including its vertical full-tile AppTile Library/Catalog
 focus model and lifecycle-bound Toast feedback.
 The first-party conformance suite passes 6/6 by building and installing the
actual Audio Mixer, Network Controls, Games & Apps, Now Playing, and YT Music
packages, launching each with the generic host in its package AppContainer, and
observing safe brokered reads/actions through simulated platform providers.
It additionally packs, installs, enables, admits, and renders a real first-party
package at the exact 1,024-directory authority limit.
 The YT case additionally exercises public validate/pack/install/enable,
 pairing, host-side private-secret persistence and Bearer injection, dashboard
 transport, lifecycle enforcement, and the absence of a trusted fallback. Pre-
 resume native fault injection remains an explicit release-test gap.
Network Controls and its Windows provider retain their focused 17/17 and 31/31
coverage for controller/focus, lifecycle/no-poll subscription ordering,
privacy/explicit state, optimistic command reconciliation, opaque identity,
native churn, cancellation, bounded failure, owner-thread disposal, responsive
GBSS, and privacy-safe real Windows read smoke.

Clean all-lane run `20260809T141527Z-8946c731` is the current authoritative
local `scripts/Verify.ps1 -Configuration Release` evidence. It passed all 41
manifest steps and 755 JUnit-adapted cases with zero failures, errors, skips,
stderr output, or stream truncation in 313.016 seconds against exact clean
commit `dc6b092`; `releaseEvidenceEligible` is true. The retained bundle records
stdout/stderr and JUnit per step, the manifest digest, 29 Community package
digests, and exact selected native-toolchain versions and hashes. The runner
includes the previously omitted WidgetTicker suite and enforces per-step
process-tree plus shared overall deadlines. This is clean local evidence, not a
referenced immutable hosted workflow artifact; hosted execution remains open.
Focused native Release suites report Declarative Layout 245 checks, Native Icons 206,
Declarative Motion 39, Overlay Targeting 42, Controller Navigation 81, Pressed
Interaction 29, Slider Interaction 2,082, Focus Navigation 41, Widget Surface
Focus 20, and Declarative Renderer 4,632; the remaining native suites also
pass. Managed feature-negotiation regressions assert the highest feature
version required by the complete tree—such as Grid v8—rather than incorrectly
pinning an inline-PNG tree to its older v6 minimum. Display-sensitive evidence
also includes 108,545 placement/render-metric/surface-geometry checks and
deterministic tiny/portrait/negative-coordinate/wide/4K and 72–480-DPI math
plus 150% font-size/letter-spacing adaptation. The platform
diagnostics suite passes 8/8 and the catalog suite passes 21/21. The packaged
hidden startup smoke remained resident for its 1.2-second observation. Physical
mixed-monitor migration/hot-plug screenshots and the broader 150% visual matrix
remain evidence gaps.

Run managed verification:

```powershell
.\scripts\Verify.ps1 -Configuration Release -Lane managed
```

Run the complete Windows verification with Visual Studio Desktop development
with C++ installed:

```powershell
.\scripts\Verify.ps1 -Configuration Release
```

## Honest limitations

- The generic native renderer handles the current declarative node kinds and
  renders YT Music, Settings, Audio Mixer, Network Controls, Games & Apps, Now
  Playing, and installed widgets through catalog descriptors. Responsive
  viewport/containment math is
  covered broadly; physical mixed-DPI, localization, accessibility, and visual
  regression evidence is still incomplete.
- Spotify now has a typed v1 broker contract, a composed trusted provider,
  and a controller-first Community addon core for public Client-ID configuration, PKCE with the exact
  `http://127.0.0.1:43827/callback/`, Windows credential-vault refresh tokens,
  player snapshots/controls, restrictions, playback events, bounded
  `Retry-After`, and sanitized errors. The local `gbar config` workflow is
  implemented and tested. The provider is composed by `WidgetBridge`; the
  addon is packaged locally through the public SDK/AppContainer path and shows
  setup guidance without opening OAuth automatically. Community package 0.2.13
  uses a compact responsive layout, puts the complete setup instructions in a
  controller VerticalScroll, and uses shared centered icon/label button
  placement rather than widget-specific offsets. Setup now shows the
  source-tree-runnable `dotnet run --project .\tools\GbarCli\GbarCli.csproj -- config ...`
  command; every explicit setup entry receives a fresh Scroll identity so a
  restored bottom offset cannot hide the heading, and **Check configuration** performs one bounded fresh configuration and
  authorization read so a newly saved Client ID takes effect without restarting
  the worker, while B remains navigation-only. An Interactive Connect that has
  already opened the system browser acknowledges the action immediately and
  retains only that exact broker request through Visible/Background using the
  widget's Created-to-Destroying lifetime. The listener waits at most fifteen
  minutes; the broker's exact Connect deadline is seventeen minutes so bounded
  token exchange, retry/backoff, and credential-vault persistence have the
  remaining two minutes. No listener exists before an explicit Connect. New
  Background controls remain denied, while revoke, Destroying, and cancellation
  still terminate the attempt. Its explicit `keep-alive` residency prevents
  idle unload from destroying this already-started authorization while the
  browser owns foreground; active polling and ordinary presentation work still
  stop outside their lifecycle. The loopback receiver tolerates at most 16
  malformed/early-close local probes inside the same fifteen-minute listener,
  while requiring loopback origin, exact host, GET/HTTP/1.1, callback path,
  and OAuth state before accepting the real callback. There is no live
  allowlisted-account evidence. Devices, queue, and playlists are implemented;
  controller-native search/text entry and broader recent/library/album/artist
  discovery remain staged work. A separately trusted singleton
  Web Playback SDK/WebView2 host now implements a bounded, hardened process and
  protocol with offline tests. Provider orchestration, PID-reuse-safe parent
  ownership, incremental `streaming` consent, Premium/EME/autoplay live proof,
  and any applicable Spotify streaming approval remain. See [Spotify
  integration status](spotify-integration.md).
  DLV-034 preserves one package-identity/OAuth/vault/token-refresh/lifecycle/
  local-playback/event authority while extracting narrow playback and collection
  endpoint families, bounded HTTP retry/rate-limit policy, and strict response
  parsing. The authority type decreased from 1,724 lines (81,619 bytes) to 1,032
  lines (48,862 bytes); extracted owners are 368, 103, and 564 lines
  respectively and receive no browser, vault, token-session, local-player, or
  event authority. Injected-response bounds now match the production 512 KiB
  transport limit. Retained focused Release run
  `20260810T140244Z-2b2f8821` completed in 12.5 seconds: Windows Spotify provider
  32/32 and PlatformBroker 51/51. Follow-up focused Release run
  `20260810T141733Z-0e24d4e1` completed in 5.3 seconds with Windows Spotify
  provider 34/34. Its manually gated backend cases prove that a
  cancellation-ignoring refresh cannot publish/cache an access token, rotate
  or save a refresh credential, or authorize the next request, and that
  disconnect stops local playback, deletes the vault entry, clears the cached
  token, and prevents later use of the disconnected session. Live-account
  evidence remains separate.
- The code clears unused native client pixels to the layered color key,
  separates Now Playing snapshot reads from live-subscription failures, loads
  the Games catalog only after Add, and preserves measured intrinsic height for
  wrapped text/buttons. The packaged Release captured after milestone
  `8e8c90a` shows the Now Playing surface over the dimmed application without
  the former opaque client rectangle and with one live GSMTC session. That is
  useful standard-viewport evidence, not proof of Retry recovery or the full
  compact/wide, DPI, contrast, transparency, and long-copy matrix. GBA-036
  through GBA-039 therefore remain Verifying.
- Local and bounded remote package/catalog commands plus CLI and Settings
  immutable-version selection/rollback are implemented. There is no
  graphical/file-picker installer, automatic release/update discovery, signed
  publisher workflow, version removal/garbage collection, or marketplace yet.
  Settings provides controller identity/version review and enable/disable after
  CLI installation. The
  bridge publishes accepted catalog changes live with last-good retention and
  safe worker reconciliation. Closed capability declarations are connected to
  the separate Settings consent flow.
- Mandatory capability-free AppContainer isolation is implemented for every
  installed/community worker. YT Music now uses that path with exact-loopback
  and write-only private-secret broker services; Settings is the only temporary
  trusted Job-only worker. Every installed worker start/restart reacquires and
  pins the complete verified path/length/hash inventory, and the AppContainer
  receives only exact non-inheriting file/traversal grants. Changed bytes and
  pre-admission insertions fail before launch; later insertions do not receive
  worker authority. Each verified content generation receives a distinct
  AppContainer identity, so a later version cannot inherit an older root grant.
  Content DACL updates use one host-only cross-process lock and profile-keyed
  schema-3 write-ahead records: unique confirmation tokens, bounded original
  DACLs, and Windows volume/file
  identities are flushed before mutation, every apply/restore uses one bound
  non-reparse handle, and a
  pending transaction must reopen the same identity before that profile starts.
  Disjoint profile records may proceed under the global mutation lock, while
  overlapping target paths or identities fail closed. Alternate application-
  package SID grants, journal corruption, identity
  mismatch, or persistence failure reject before launch. Catalog session handles
  now supply their volume/file identities through one typed bridge lease target
  sequence; runtime requires exact consistent evidence and compares its bound
  handles before journal publication or DACL mutation. The host-only recovery
  service is reachable only through the exact trusted Settings diagnostics
  companion or the local CLI; both require a fresh exact confirmation token and
  expose no force-clear or replacement authority. Handle-relative ancestor
  traversal and one aggregate ACL/handshake start deadline remain open.
  Publisher
  signing/revocation, CPU quotas, disk/profile quotas and cleanup, provider
  hardening, and the security audit/history UI are not production-ready. The
  narrow Core Audio and Windows network backends still need broader hardware/
  privacy/churn and hidden-state performance evidence. Isolation plus
  capability transport/consent does not establish a public publisher-trust
  boundary.
- Win32k system-call disable is not enabled for managed workers because the
  tested mitigation caused CoreCLR DLL initialization failure (`0xC0000142`).
  Job Object UI restrictions remain enabled.
- Closed audio/network/Bluetooth/recent-activity manifest permissions are
  enforced through the broker, as are the media-session read/control
  permissions and the narrowly bounded dashboard gesture exception for one
  exact control operation.
  Community AppContainers have zero OS capabilities/network authority and no
  general desktop token; direct resource access is limited to the generic
  runtime plus exact session-verified package files. OS capability APIs remain
  brokered.
- `gbar render` accepts only bounded data-only snapshots. Named credential-free
  scenarios now execute in a dedicated AppContainer/Job worker with an exact
  pinned content lease, typed fake services, normal lifecycle, deterministic
  repeated-snapshot validation, sanitized diagnostics, and bounded teardown;
  the CLI process never loads author assemblies. `gbar dev` remains the
  interactive production-host integration path.

  The DLV-108 focused Release evidence passes the new discoverable
  `MSTest.Sdk` 4.3.2 scenario suite 6/6, CLI 56/56, Widget SDK 88/88, public API
  compatibility 12/12, generic worker 10/10, and documentation across 58
  Markdown files. The Runtime suite's first 73/74 run hit its unchanged
  cancellation-ignoring gesture-revoke race; one bounded no-build rerun passed
  74/74, including exact content identity, AppContainer authority, Job process-
  tree kill-on-close, lifecycle, malformed snapshot, and hung destruction.

  The optional DLV-109 `WidgetScenario` test helper builds on that contract with
  named lifecycle/action/assertion steps, structured failures, and a generic
  bounded typed-operation barrier. Basic action and media/data consumers prove
  focus/text/busy assertions, delayed completion, denied/revoked responses,
  latest-wins stale rejection, deactivation, and zero continuing fake work
  without sleeps or hidden polling. Focused Release evidence passes the combined
  scenario suite 9/9, Widget SDK 88/88, public API compatibility 12/12, and the
  documentation contract across 58 Markdown files.
- Universal controller suppression is unresolved for games using background
  Raw Input or direct HID access.
- The quarantined XInput Guide fallback depends on an undocumented system-DLL
  ordinal/bit and may vary by controller model, mode, firmware, and transport.
- True Fullscreen Exclusive, HDR, physical mixed-DPI/hot-plug coverage,
  anti-cheat compatibility, latency, and steady-state resource budgets need a
  larger measured matrix.
- Topmost/foreground reassertion is best effort. Secure desktop, elevated
  windows, exclusive render paths, and multi-monitor backdrop coverage are not
  supported contracts.
- The manifest protocol retains `keep-alive` as its compatibility default, but
  the controller scaffold and Clock sample now select five-minute idle unload.
  The bridge atomically caps application residency at eight worker sessions,
  refuses overcommit without evicting pinned workers, reports optional memory
  guidance without treating it as reserved capacity, and preserves a separately
  reported trusted Settings control-plane slot. Per-widget measured telemetry,
  user overrides, critical-work leases, packaged resource measurements, and
  long-duration churn remain open.
- Recent activity observation starts lazily on the first authorized read, then
  remains event-driven until the bridge/backend is disposed. Consent revocation
  blocks delivery and activation and cancels in-flight broker requests, but
  immediately stopping provider-side observation and clearing its bounded
  in-memory history on read-consent revoke is future hardening.
- GBSS compilation and bridge-global layering are implemented. `selected`,
  `disabled`, and `busy` snapshot state participates in the complete
  `base`/`focused` maps. The bridge also publishes a transient `pressed` map;
  the native host applies it only while the exact physical controller action
  remains down and reconciles it across snapshot/focus changes. Stable node
  opacity/scale/translation targets animate through bounded native transitions.
  Translation uses shared subtree paint/clip/focus/hit/navigation/Scroll
  geometry without changing layout. The shell now opens with a bounded 140 ms
  ease-out opacity track, closes with a 100 ms ease-in track, and reveals a new
  or replaced widget identity from subtle 0.78 opacity over 100 ms. Reversals
  retarget from the presented value, ordinary same-identity snapshots never
  flash, entering widget focus snaps content visible, reduced motion snaps and
  cancels, and settled/hidden state schedules no idle frames. Packaged visual/
  frame-time evidence and future dynamic semantic states remain open.
- Controller Settings, strict persistence, version-pinned theme selection,
  no-poll watching, last-good revisions, and globally layered widget styles are
  implemented. Safe theme scaffold/validate/computed-preview/pack/inspect/
  install/list tooling and native post-cascade contrast/bold/reduced-
  transparency policy are implemented. Native graphical preview, signing,
  removal/update/rollback, auto-scroll, screen-reader/magnification integration,
  and the physical combined 150% accessibility matrix are not. See [settings
  and global themes](settings-and-themes.md) and [theme packaging and
  distribution](theme-packaging.md).
- Audio Mixer is a bounded first-party public-SDK reference slice: its widget,
  package/catalog integration, and event-driven per-session Core Audio provider
  exist, while the controller-first **Audio Control** roadmap remains
  incomplete. It now includes explicit-capability default-microphone mute/level
  and sanitized default-device visibility, but supported output/default-role
  selection remains unresolved and microphone sample capture is out of scope.
  Network Controls is likewise a bounded
  reference slice with provider, worker, catalog, controller, and packaging
  evidence—not a completed **Network Control** product widget. Its roadmap adds
  privacy-gated identity/link details, bounded IP/gateway/DNS summaries,
  measured throughput/latency/loss diagnostics, and reviewed recovery actions.
  The implemented reference includes precise-location-gated explicit
  available-network scans and current saved/open result connections; it
  includes host-owned masked WPA2/WPA3 Personal entry and exact per-user profile
  creation without exposing secret/profile XML to the worker, but excludes
  enterprise/legacy provisioning and automatic current SSID/signal access.
  Software Wi-Fi radio control plus separately capability-gated
  Bluetooth radio/discovery and explicit association pairing/removal are
  implemented. Remaining roadmap work includes profile-specific Bluetooth operations.
  Enterprise Wi-Fi and generic Bluetooth
  Connect/Disconnect are not promised. See
  [widget capabilities](capabilities.md) and [Windows provider
  architecture](windows-provider-architecture.md) and [Network Controls
  reference](network-controls.md).
- The DLV-028 managed split keeps `NetworkControlsWidget` as the only
  lifecycle, host-command, committed-state, and invalidation owner. Provider
  normalization/merge, command admission/feedback, closed action routing, and
  snapshot-only presentation now have named internal boundaries. Coordination
  is one state lock, one command semaphore, one active-run generation, and one
  SDK-owned `Active` latest-operation lane; the former field cancellation
  source and two detached provider-observer roots were removed. Policies and
  presentation share no mutable state with the widget. Bounded dirty-worktree
  Release run `20260810T113815Z-8a7cd592` passed the SDK build with zero
  warnings/errors, Widget SDK contracts 84/84, Network Controls 22/22, and the
  generic first-party AppContainer/worker seam 6/6 in 74.8 seconds. This is
  focused implementation evidence, not a canonical clean-worktree aggregate.

## Diagnostics

- DLV-085 adds a document-blind per-widget local-data reset to trusted Settings.
  The authenticated Settings companion carries only sanitized inspection/result
  values and an opaque revision-bound confirmation token. The existing owners
  remain singular: Settings owns catalog selection and presentation,
  `BridgeClientRegistry` retires and replaces the exact worker generation, and
  the private-state backend alone reads or clears documents. The new adapter
  operates only while the registry slot is reserved, restores the prior
  lifecycle with a fresh generation, and leaves neighboring widget state
  untouched. No ordinary widget capability, public Widget SDK/protocol field,
  filesystem deletion, or second lifecycle coordinator was added.
  Focused Release evidence passes Runtime 74/74, Windows Community private
  state 10/10, Widget Catalog 35/35, Settings 54/54, the authenticated
  diagnostics transport 16/16, WidgetBridge 73/73 (including the composed
  installed generic-worker clear/restart and neighbor-isolation path), and 55
  documentation files. The retained runs are
  `20260811T171804Z-8a4d3c74`, `20260811T172739Z-a6d4760b`, and
  `20260811T173042Z-5a23c43d`; the first Bridge attempt timed out after a
  replacement-cancellation regression, which was corrected before the final
  green Bridge run.

- DLV-089 makes disabled installed-widget local-data management reuse the same
  canonical `installed.<package-version-hash>` instance identity that
  `BridgeCatalog` assigns while the exact package version is enabled. One
  internal derivation owner now serves both paths; the former synthetic
  `<widget-id>.disabled` namespace is removed. The production-shaped Bridge
  fixture writes through the enabled identity, disables through the real
  catalog, rejects a stale confirmation, clears without creating a worker,
  preserves a neighboring widget, and observes clean state after re-enabling
  the unchanged version. No private-state schema, public protocol, package
  enablement, or removal behavior changed.

- DLV-100 closes the protocol-v15 TextEntry bridge-style admission gap at the
  canonical computed-style role switch. `textEntry` now resolves as a distinct
  closed GBSS role, null/default themes still emit an empty style entry keyed by
  the stable node ID, and unknown node kinds retain an exact fail-closed bridge
  diagnostic. The production-shaped installed AppContainer route admits both
  the Game Launcher search entry and Network Controls protected entry, then
  commits a Game Launcher search through the existing action path. Focused
  Release evidence passes WidgetBridge 77/77, Game Launcher 45/45, Network
  Controls 24/24, the exact installed TextEntry route, and 55 documentation
  files. The first broader installed attempt continued past this milestone into
  an unrelated unavailable-launch workflow; the final acceptance mode is
  intentionally bounded to TextEntry admission and committed search.

- DLV-103 makes Now Playing activation and Retry one SDK-owned Active
  SingleFlight generation. The widget opens the changed-event subscription
  before its current snapshot read, rejects cancellation-ignoring late reads or
  events, treats an empty snapshot as success, retains last-good sessions when
  refresh or the live channel fails, and exposes one reconnect action without
  creating a detached task/lifecycle owner. Bridge records only bounded,
  transition-deduplicated Media Sessions stage/code diagnostics in
  `overlay.log`; player/session identity, media metadata, provider bodies,
  process details, and credentials remain excluded. A production-shaped
  installed AppContainer route covers success, empty, lifecycle channel
  replacement, transient failure followed by Retry, stale completion, and
  teardown. Retained focused Release run
  `20260812T010704Z-4039ada8` passed Widget SDK 87/87, PlatformBroker 55/55,
  Windows Media 13/13 (including one sanitized live GSMTC probe), Now Playing
  20/20, WidgetBridge 79/79, and 57 documentation files in 37.8 seconds. The
  separate bounded installed route passed from the same final production/test
  implementation. This is scoped dirty-worktree evidence, not a canonical
  aggregate or a substitute for the user's next packaged visual check.

- DLV-119 traces the accepted-Release **Media sessions could not be loaded**
  recurrence to the widget's untyped exception fallback: the host recorded
  current Now Playing snapshots but no transition-only broker/provider
  diagnostic, so provider authority and transport protocol remain unchanged.
  Now Playing projects a bounded `snapshot-read`, `subscription-open`, or
  `subscription-read` stage plus a sanitized typed code, including safe
  `request_timeout`, `request_canceled`, and `unexpected_failure` fallbacks.
  Retry still owns one fresh Active generation; transient failure preserves
  last-good rows and controls, and exception messages, paths, response bodies,
  player identity, and process details remain excluded. Focused Release evidence
  passes Now Playing 23/23, Widget SDK 88/88, Windows Media 13/13 (including one
  sanitized live GSMTC probe), and the documentation contract across 59 files.

- DLV-162 audits the current credential-free Now Playing recovery path against
  the local `overlay.log` interval from 06:32:14.192 through 06:32:15.915. The
  host retained the prior surface until current Now Playing snapshots 1 and 2
  were admitted, with no Media Sessions failure diagnostic. The live GSMTC
  probe observed one sanitized session. One provider edge was reproducible:
  an identity-less Windows session threw while deriving its friendly label and
  was omitted. The native adapter now assigns the fixed `Media app` fallback
  without exposing identity or weakening opaque control authority. Focused
  Release evidence passes Windows Media 14/14 and Now Playing 23/23; the single
  installed AppContainer route also passes initial/empty snapshots, transient
  failure and Retry, stale-completion rejection, reactivation, and teardown.

- DLV-164 corrects the Game Launcher Experience controller-hints slot at its
  managed projection owner. The `.game-launcher-footer` wrapping style now
  belongs to a non-scrolling row rather than a stack, so strict native style
  validation no longer has to ignore `flex-wrap`. All four launcher experiences
  retain identical hint, action, focus, and SavedId authority across compact,
  standard, and wide projections at 100% and 150% text scale. Focused Release
  evidence passes Game Launcher 77/77. The existing production-host preset and
  fallback route passes, and a separate ordinary installed diagnostic run
  admitted current Game Launcher sequence 2 with zero
  `game-launcher.slot.controller-hints` `invalid_style` records.

- DLV-107 corrects the accepted DLV-025 composition alpha and motion contract.
  The overlay HWND is now one `WS_EX_NOREDIRECTIONBITMAP` transparent container;
  the existing Windows-10-compatible DirectComposition owner clears its
  premultiplied surface to alpha zero, paints only authored panel/tray pixels,
  and uses one effect-group opacity owner while the separate backdrop remains
  the sole dimming layer. A complete detached destination surface is committed
  before a bounded 140 ms visual transform; motion ticks commit only transform
  state with no `WaitForCommitCompletion`, surface redraw, sleep, or HWND
  resize. Pointer hit testing and UI Automation bounds share that presented
  transform, and unused client pixels return `HTTRANSPARENT`. The existing
  color-key HWND renderer remains the one explicit fallback and is enabled only
  after the composition owner is reset. Focused Release evidence passes the
  native host build, Placement 108547, Targeting 64, Transition 56, Chrome 45,
  Accessibility Provider 152, Focus Navigation 49, Host Accessibility 34, and
  Declarative Renderer 4777 checks. The production-host temporal matrix passed
  Audio Mixer, Game Launcher, Now Playing, Games & Apps, Network Controls,
  YT Music, Spotify, and Settings with eight complete destination samples:
  draw max 2681 us, surface commit max 1491 us, fixed-container placement max
  1641 us, and nonblocking visual commit max 68 us. No aggregate, capture,
  screenshot, delay, public protocol change, or second renderer was used;
  packaged visual confirmation remains the planner/user gate.

- DLV-104 gives Game Launcher one explicit vertical-layout contract without
  increasing its preferred surface. Header, source status, query controls, and
  footer actions are fixed chrome; `game-launcher.library.scroll` alone owns
  the remaining vertical extent and stable collection offset. Narrow or short
  surfaces use local compact title/source-summary branches, while query and
  action strips remain horizontally reachable. The Library, Add games, Add
  running app, Hidden, warm, loading, error, and empty routes retain one
  semantic tree and the existing collection/focus identities. Focused Release
  coverage passes Game Launcher 46/46 across minimum/standard/wide profiles at
  100/125/150 percent, including first/middle/last collection reachability and
  deterministic reopen. The unchanged native renderer and shared-component
  geometry dependencies pass 4,777 and 589 checks respectively. The public
  `gbar pack` path also produced the unchanged Game Launcher 0.6.0 package (10
  files, 803,117 bytes). No native layout semantics, preferred-height increase,
  provider behavior, or screenshot acceptance was introduced.

- DLV-114 adds one bounded Game Launcher details route without adding provider,
  lifecycle, persistence, or launch authority. View selects an exact current
  SavedId and immutable return-focus identity; a separate pure details policy and
  presenter project full title, normalized source, availability, launch evidence,
  favorite, group, and preferred state. A reuses fresh exact-SavedId launch
  resolution, X/Y reuse favorite/hide policy, and LB/RB retain their existing
  single-page group/prefer or multi-page traversal meaning. B restores the
  originating tile and retained collection offset. Removed/replaced identities
  become unavailable and cannot route actions to a same-title neighbor. Focused
  Release evidence passes Game Launcher 50/50, including duplicate titles,
  disappearing identities, busy/unavailable action state, exact Back focus,
  page-bumper non-overlap, long labels, and deterministic bounded rendering.

- DLV-117 corrects the details variant action without changing organization
  semantics. The first activation is labeled **Choose another variant**, names
  the selected game in visible status, and returns to the exact Library tile so
  a distinct second game is reachable. A valid second details route labels the
  single operation **Group with selected game** or **Remove from variant group**
  and retains committed success/failure feedback. Same and stale identities fail
  closed; successful hide still closes details only after the exact persisted
  exclusion is observed. Game Launcher remains the sole lifecycle, committed
  state, provider, and persistence owner; the pure details projection gains no
  task, lock, timer, or resource. Focused Release evidence passes Game Launcher
  55/55, including exact CAS replay and canceled second-selection refusal.

- DLV-102 gives trusted app artwork an explicit bounded terminal-unavailable
  state in the existing native image cache. One bridge result moves every
  matching widget/handle entry once, posts one invalidation, and emits one
  sanitized `widget`/opaque-`handle` transition diagnostic; later paints,
  focus changes, and same-snapshot republishes do not report `image_failed`.
  Shared Image/AppTile rendering keeps pending artwork neutral, uses the same
  closed Play glyph as the authored no-artwork tile when resolution is
  terminal, and preserves the tile's layout, focus, hit-test, action, and UIA
  geometry. A new opaque revision evicts the prior node revision and supplied
  pixels replace the fallback; stale results cannot do so. Focused Release
  evidence passes RemoteImageCache and DeclarativeRenderer (4,839 checks),
  including production-shaped Game Launcher and Games & Apps available,
  pending, unavailable, repaint, republish, stale-result, recovery, raster,
  semantics, and cache-bound cases, plus the OverlayHost Release build. No
  provider discovery, public protocol, compositor, screenshot, live Steam, or
  aggregate verification was used.

- DLV-033 extracts the bridge-facing widget-session policy from `OverlayApp`
  into one directly tested native `WidgetSessionCoordinator`. The logical
  responsibility map is:

  | Concern | Before DLV-033 | After DLV-033 |
  | --- | --- | --- |
  | Catalog identity and generation | `OverlayApp` owned descriptors plus inline runtime/presentation-generation reconciliation. | `WidgetSessionCoordinator` owns descriptor replacement/removal and rejects completions outside the current instance/runtime/presentation generation. |
  | Snapshot and session status | `OverlayApp` owned snapshot and startup-failure maps and performed synchronous bridge admission. | `WidgetSessionCoordinator` owns last-good snapshots and typed start, snapshot, protocol, lifecycle, and restart status; `OverlayApp` consumes narrow value events. |
  | Lifecycle and retry policy | `OverlayApp` owned lifecycle state, catalog retry attempts, and direct lifecycle calls. | `WidgetSessionCoordinator` owns targets, bounded drain, retry policy, restart retirement, and late-completion rejection. |
  | Request execution | The UI thread issued catalog, presentation, lifecycle, snapshot, and restart requests directly through `WidgetBridgeClient`. | One bounded 32-request coordinator queue serializes bridge work off the UI thread, supports cancellation, and posts typed completions; bridge event polling remains nonblocking while a request is stalled. |
  | Host authority | Session policy and Win32 presentation effects were interleaved in `OverlayApp`. | `OverlayApp` remains the sole HWND, focus, input, renderer, D2D/DWrite, accessibility, and presentation adapter; `WidgetBridgeClient` remains the pipe/protocol transport. |

  `OverlayApp` falls from 6,333 to 6,166 physical lines and no longer declares
  the descriptor, snapshot, startup-failure, lifecycle-state, or catalog-retry
  collections. Focused Release evidence passes the coordinator's six
  deterministic scenarios, OverlayState, lifecycle, WidgetBridgeClient catalog
  revision, and 305 failure-feedback checks, plus the native Release host. The
  production-host retained-content matrix passes all eight widget identities
  with draw max 2,712 us, commit max 1,391 us, coordinated geometry max 1,516
  us, and nonblocking motion commit max 57 us. The two-session fixture retains
  a responsive session and host-owned Close/Guide intent while its neighbor is
  stalled. No public protocol, renderer, compositor, focus graph, UI behavior,
  screenshot, or aggregate work was added.

- DLV-112/116 establish one host-owned local widget package import operation
  for the exact bundled Settings presentation, without granting filesystem or
  installer authority to the worker. The responsibility and evidence map is:

  | Concern | Before DLV-112/116 | After DLV-112/116 |
  | --- | --- | --- |
  | Visible action admission | No native consumer could distinguish a future Settings install action from ordinary worker input. | One private `LocalWidgetPackageImport` contract claims only `host.install-local-widget` from `installed.install-local` in `installed.widgets`, bound to the current rendered node, snapshot instance, bundled package/publisher identity, runtime/presentation generations, and Interactive lifecycle. Controller, pointer, and UIA use that same admission seam. |
  | Picker and operation lifetime | No host-owned local package picker or exact operation owner existed. | The native owner serializes one `.gbarwidget` picker and one submitted operation, revalidates origin after the modal interval, cancels on overlay close/shutdown, ignores stale completions, and exposes only path-free status. |
  | Package publication | Local installation was available only through CLI flows. | The bridge opens one locked non-reparse regular-file stream and uses the existing `WidgetCatalog` installer and catalog lock; successful publication remains disabled and emits one catalog revision. No worker receives the selected path. |
  | Verification | Native completion assertions added with DLV-112 used `assert` and disappeared under Release `/DNDEBUG`; no production caller seam was exercised. | Release-hard checks cover exact action admission, forged identities, generations, scope/action/source, lifecycle, repeat/cancel/stale operation handling, malformed/wrong-operation/path-bearing completion frames, and path-free results. Managed import-prefix and catalog scenarios cover invalid, duplicate, stale, cancellation, locked-source, reparse, disabled publication, and revision behavior. |

  DLV-113 adds the visible **Install local widget** consumer to Installed
  Widgets. Its exact action/source/scope tuple is host-owned; direct or forged
  worker actions remain inert. The controller-scroll presentation keeps B and
  stable focus, explains disabled review and path isolation, and reloads a
  host-published package through the existing activation boundary. The control
  does not auto-enable packages, add remote acquisition, or create a public
  capability. Focused Release evidence passes Settings 56/56, WidgetBridge
  82/82, and the documentation contract across 59 Markdown files.

- Latest overlay initialization error:
  `%LOCALAPPDATA%\GameBarAlternative\startup-error.log`
- Overlay order/last-widget state:
  `%LOCALAPPDATA%\GameBarAlternative\overlay-state.ini`
- Input-probe logs: timestamped in the launch directory by default, or the
  explicit `--log` path.

See the [documentation index](README.md), [security and trust](security-and-trust.md),
and [troubleshooting](troubleshooting.md).

### Advanced scaffold profiles (DLV-110)

`gbar new widget --template` now selects one strict version-2 inventory entry:
`basic`, `data`, `media`, or `multipage` (with `basic` as the compatible
default). The generated sources demonstrate the public lifecycle/state,
`WidgetResource`, `WidgetOptimisticCommand`, and responsive
`WidgetNavigator`/`NavigationShell` paths without credentials, repository
references, or first-party-sized classes. Every profile includes a sibling
`MSTest.Sdk` 4.3.2 semantic test and the matching isolated `gbar preview`
scenario declaration. Selection and generation remain one bounded staging-and-
rename transaction; invalid selection and malformed inventory input publish no
partial target. Focused Release evidence exercises all four external-directory
journeys through build, one discoverable test, isolated preview, validation,
and repeated byte-identical package creation.

### Disabled Community package uninstall (DLV-123)

Settings Installed widgets now exposes **Uninstall widget** only for a current
disabled Community identity. The nested confirmation starts on Cancel, returns
focus to the uninstall row, displays the bounded package identity without its
opaque token, and explains that package versions are removed while local data,
credentials, provider data, themes, settings, and user files are retained.
Built-in and enabled packages expose no uninstall action; **Clear local data**
remains a separate confirmation. Success refreshes the installed list and keeps
exactly one **Install local widget** action available. Stale, resident, pending-
cleanup, recovery-pending, unavailable, and refused outcomes remain bounded and
retryable without exposing paths.

Responsibility remains singular across the changed boundary:

| Concern | Before | After |
| --- | --- | --- |
| Installed-widget selection and presentation | `SettingsInstalledWidgetPolicy` owned value-only selection/paging; `SettingsInstalledWidgetPresentation` owned details and local-data confirmation. | `SettingsInstalledWidgetUninstallPolicy` owns exact disabled-only admission/cancel/focus policy, and `SettingsInstalledWidgetUninstallOperation` owns the inspect/revalidate/mutate request sequence plus closed result interpretation. The existing presenter owns uninstall details and retention copy; `SettingsWidget` still solely commits page/status state and serializes actions. |
| Package identity and mutation | `WidgetCatalog.UninstallAsync` owned disabled-only atomic package retirement for CLI callers. | `WidgetCatalog` also issues and revalidates a SHA-256 token over publisher authority, active version, enabled state, and every installed version digest under the same operation lock. No path enters the managed capability. |
| Worker/channel/catalog lifecycle | The authenticated Settings companion exposed diagnostics, authority recovery, and local-data management; `BridgeCatalogMonitor` published semantic reloads. | `BridgeWidgetPackageUninstallService` is the sole path-free uninstall adapter. It invokes catalog retirement, maps closed outcomes, and forces one catalog revision even when removal concerned an already-disabled package absent from the runtime catalog. Registry and native lifecycle ownership are unchanged. |

Focused Release evidence passes WidgetCatalog 35/35, PlatformDiagnostics 17/17,
Settings 57/57, and WidgetBridge 83/83. The generic-worker and documentation
groups plus the required single exact-commit canonical checkpoint are recorded
with the closing commit evidence.

### Game Launcher control and collection continuity (DLV-126)

Game Launcher now derives every warm replacement anchor from the exact rendered
non-hidden rows instead of the unfiltered private display projection. Add games,
Add running app, Hidden, Favorites, Recent, Source, Sort, and Clear therefore
retain protocol-valid current snapshots while their managed replacement is in
flight and after it commits. Current game tiles publish View/X/Y and contextual
LB/RB shortcuts only while those actions are enabled; the matching visible help
is removed during launch or organization work and restored with actionability.
The existing host-owned scroll-edge contract remains unchanged: one Down-edge
cursor action appends the next bounded provider page and requests an entering
game focus, while final partial rows expose no looping continuation. Focused
Release evidence passes Game Launcher 60/60. No Widget SDK, protocol, provider,
native host, or launch-authority contract changed.

### Game Launcher built-in hero rail (DLV-128)

The Library route now composes one bounded selected-game hero above one
horizontal cover rail while preserving the existing installed-only cursor,
query, organization, details, and exact-SavedId action paths. Controller
Left/Right projects the adjacent bounded rail identity into private in-memory
selection state before native focus movement; it never calls the launch
capability. A, View, X, Y, LB, and RB remain authored on the exact focused tile.
The hero reuses only the selected row's opaque trusted artwork handle and falls
back to a semantic Play glyph; snapshots and private state contain no URL, path,
image bytes, or provider identity.

Responsibility changed from `GameLauncherPresentation` owning Library ordering
and three responsive grids to `GameLauncherHeroRailPolicy` owning the bounded
recent/manual/catalog rail projection plus deterministic nearest-index fallback,
and `GameLauncherHeroRailPresentation` owning hero semantics and fallback.
`GameLauncherWidget` remains the sole lifecycle, cursor, committed organization,
launch, and invalidation owner; its only new responsibility is the two-field
in-memory hero selection and controller admission that feeds the pure policy.
There is no new task, lock, resource, capability, protocol, or native layout
owner. Focused Release evidence is recorded with the closing milestone commit.
The root moves from 1,279 to 1,350 physical lines because it remains the one
cohesive lifecycle/cursor/capability/action owner and now admits focused-element
selection; the general presenter falls from 747 to 694 lines, while the new
271-line hero policy/component contains the extracted ordering and composition.
Final focused evidence passes Game Launcher 62/62 and the documentation contract
across 61 Markdown files.

### Complete scenario verification discovery (DLV-125)

The canonical verification manifest now represents the existing
`WidgetScenario.Tests` `MSTest.Sdk` 4.3.2 project exactly once. Its dedicated
managed step uses the bounded Microsoft Testing Platform invocation, requires at
least nine discoverable cases, preserves the current result schema and lane
selection, and changes no existing step ID or timeout. The verifier self-test
passes complete project discovery and fail-closed result fixtures; the scenario
project passes 9/9 both directly and through the manifest-selected runner. The
required single clean exact-commit aggregate is closing-commit evidence rather
than a dirty-worktree run.

### Work-area-fitted shared widget shell (DLV-127)

Widget-to-widget presentation now keeps one host-owned shell and tray rectangle
fitted to the selected monitor's live `rcWork`, effective DPI, and interface
scale. Compact, adaptive, standard, wide, and explicit dimensions remain
logical body hints: width and height are clamped inside the shell above the
stationary guide/tray, and declarative Scroll remains the overflow contract.
Retained cold-start content and admitted destination content therefore share
exact shell/tray/selected-tile bounds without a six-to-eight capacity change or
a whole-shell composition transform.

Responsibility remains singular across the changed boundary:

| Concern | Before | After |
| --- | --- | --- |
| Monitor and HWND placement | `ShowOverlay` fitted a fixed 1180×700 DirectComposition container to `rcWork`, but separately placed a widget-selected source extent that could exceed that container/work area. | `ShowOverlay` remains the only monitor/work-area/DPI owner and fits one shared widget shell. The existing DirectComposition surface remains the only complete-content presentation owner. |
| Widget sizing | `DesiredPresentationExtentDip` promoted per-widget surface hints into shell/HWND extent and composition-motion authority. | `DesiredPresentationExtentDip` supplies the shared shell; `DesiredWidgetSurfaceTarget` supplies only preferred body width/height to `ComputeOverlaySurfaceGeometry`, which clamps the body before the stationary tray. |
| Tray paint, input, and semantics | All three consumed one `TrayLayout`, but widget-selected shell width/height changed that layout and its visible capacity during a switch. | All three still consume one `TrayLayout`; retained and admitted frames now have identical shell/tray/selected bounds, while catalog mutations remain the only reason total/visible identities can change. |

Focused Release evidence passes 111,253 placement checks, 64 targeting checks,
63 transition checks, 45 chrome checks, 152 accessibility-provider checks, 49
focus checks, 34 host-accessibility checks, 4,839 renderer checks, and 54 tray-
layout checks. The production `WidgetSwitchHostTests` fixture passes eight real
OverlayHost transitions across compact, Game Launcher wide, and Spotify
adaptive bodies; it records zero widget-shell motion commits, live placement
re-resolution, contained `rcWork` bounds, and maxima of 3,023 us draw, 1,339 us
commit, and 1,510 us coordinated geometry.

### Deterministic Gbar dev cancellation fixture (DLV-129)

The retained clean aggregate `20260812T074036Z-37ce8dc0` passed 57/58 Gbar CLI
cases and timed out only while the dev retention fixture waited 30 seconds for a
cold nested Release generation after preceding dev/process-tree cases. An exact
isolated run passed without production changes, identifying cross-test build
scheduling rather than a dev-session lifecycle defect.

The fixture now prepares one catalog-valid package directory using the existing
isolated builder and unchanged 90-second product build deadline before starting
its lifecycle assertions. A new exact-name runner filter executes only this case;
ordinary complete-suite discovery and counts are unchanged. Three consecutive
bounded focused runs pass 1/1 and prove authenticated Ready publication,
last-good retention after an invalid declared style change, caller cancellation,
temporary catalog cleanup, and child-host Job exit. Persistent build-server
arguments remain covered by their existing separate test. No production dev
session, timeout, verifier schema, or aggregate behavior changed.

### Normalized app-library presentation contract (DLV-130)

The app-library capability now publishes one versioned immutable presentation
per opaque AppId/SavedId pair. Item/source identity, closed availability,
role-keyed artwork, optional attributed metadata, closed capabilities, and an
optional operation are separate focused values; the removed title/kind/source/
artwork scalar constructors and accessors have no compatibility facade. The SDK
validates the complete shape, cross-checks explicit launchability with the
Launch capability, and rejects unknown versions, enums, duplicate roles or
actions, unsafe provenance, and malformed operations as `malformed_response`.
Game Launcher and Games & Apps consume this same model, keep retained rows
explicitly non-authorizing, and enable launch only after fresh exact resolution.

The trusted provider supplies only currently proven installed/source/artwork
facts. Broker projection preserves an exact sanitized page source ID when one
exists and never exposes provider identity, paths, commands, AUMIDs, Steam IDs,
or artwork bytes. During installed acceptance, a no-artwork row exposed that the
registry had generated a handle with an empty revision; registry admission now
keeps that row's artwork set empty and retires any prior handle instead. The
production Bridge fixture directly covers a multi-source Game/Application page,
including the no-artwork Game fallback.

Retained dirty-worktree Release run `20260812T085816Z-dc9417d9` passes Widget
SDK 88/88, API compatibility 12/12, Windows app-library provider 62/62, Games &
Apps 59/59, Game Launcher 62/62, Bridge 83/83, and 61-document validation. After
the empty-artwork correction, focused run `20260812T091559Z-f1574fba` passes
broker 56/56 and Bridge 83/83. The exact bounded command
`dotnet run --project tests/FirstPartyWidgetConformance.Tests/FirstPartyWidgetConformance.Tests.csproj --configuration Release -- --games-apps-installed-acceptance`
passes the installed generic-worker/AppContainer Games & Apps route. No canonical
aggregate, native layout, external provider, credential, content-operation, or
package-schema work was run or changed.

### Supported Windows and Xbox installed-game source (DLV-138)

The trusted Windows app-library provider now includes a separate installed
Microsoft/Xbox package-game source beside conservative Start Menu/AppsFolder
applications and Steam. It enumerates current-user packages through the
supported Windows `PackageManager` API and classifies an exact registered
application as a Game only when a bounded, non-reparse `MicrosoftGame.config`
names that application ID. Microsoft documents that configuration as the
game-specific packaging manifest and its `Executable Id` as the application ID;
titles, icons, package names, and locations are never classification evidence.

The source keeps package family/full name, install location, AUMID, and
configuration bytes provider-private. It normalizes the exact AUMID to the same
stable identity as AppsFolder, so duplicate launch registrations collapse while
distinct application variants remain separate. Refresh commits add/remove/
update generations atomically. A failed package read retains bounded last-good
display with explicit unavailable source health but clears exact source
authority, and launch must reread the same package generation, application ID,
and game evidence before constrained packaged activation.

Focused Release verification covers installed/non-game/mismatched evidence,
AppsFolder deduplication, multiple variants, add/remove/update, failure-retained
display without authority, exact launch revalidation, cancellation, and catalog
bounds in the Windows provider suite. Games & Apps and Game Launcher fixtures
cover automatic Game projection, exact source attribution, semantic artwork
fallback, and opaque launch routing. No network, credential, account-owned game,
install/update operation, external helper, native, capture, or aggregate work is
included.

### Opt-in Epic installed-game source (DLV-139)

Settings now exposes a controller/keyboard-reachable **Game sources** page with
an explicit Epic installed-games toggle, persisted in the host-owned platform
settings document. When disabled the provider performs no Epic manifest I/O
and reports `source_disabled`. When enabled, the normalized Windows app-library
provider reads only the fixed ProgramData Epic installed-manifest directory,
with bounded count/bytes/depth and strict format-version, duplicate-property,
identity, non-reparse install path, relative executable, and stable-read checks.

Epic AppName, catalog namespace/item ID, manifest path, install location, and
executable evidence remain provider-private. The provider derives its own
stable identity, and the broker continues deriving the authority-scoped
SavedId. Refresh generations reconcile additions/removals/updates independently
of Windows, Microsoft/Xbox, and Steam. Disabled, unavailable, and corrupt states
are explicit; stale display can never mint launch authority. Activation rereads
the exact manifest and executable revision and only then constructs the fixed
Epic launcher URI from validated components. No login, credentials, network
library, client automation, installation, update, uninstall, arbitrary command,
public protocol change, helper binary, native work, capture, or aggregate is in
scope.

Focused Release evidence passes Windows app-library provider 71/71, platform
settings 16/16, Settings 58/58, Games & Apps 61/61, Game Launcher 64/64,
WidgetBridge 83/83, and the 61-document contract. Authored local fixtures cover
disabled/no-read, valid, duplicate, unknown-schema, unsafe-path, changed-during-
read, add/remove, stale launch, unavailable last-good display, exact status, and
constrained URI cases. The normalized cross-process DTO did not change, so no
installed-worker rerun was required.

### Opt-in GOG installed-game source (DLV-147)

Settings now exposes a separately persisted **GOG installed games** toggle under
**Game sources**. The production Bridge reads that host-owned preference when it
constructs the trusted Windows app-library provider. Disabled mode returns
`source_disabled` before opening a GOG registry key or file. Enabled mode reads
only the fixed machine-wide `SOFTWARE\GOG.com\Games` root in the 32-bit and
64-bit Windows registry views, bounded to 4,096 records, and requires the exact
matching `goggame-<product-id>.info` file. This is bounded best-effort installed
evidence rather than an official GOG API or exhaustive catalog.

The responsibility change is source-local rather than another provider/widget
switch:

| Responsibility | Before | After |
|---|---|---|
| Fixed registration access | No GOG owner. | `WindowsGogRegistryReader` alone opens the two fixed machine registry views and returns bounded trusted records. |
| Registration validation and exact refresh | No GOG owner. | `GogInstalledGameApplicationSource` owns opt-in admission, strict numeric identity, safe install/info-file evidence, duplicate/mixed health, stable reads, and exact reread. |
| Normalized catalog and authority | The provider composed Windows, Microsoft/Xbox, Steam, and Epic sources. | `GogGameLibrarySource` implements the same private source contract and owns GOG attribution, opaque identity, last-good display, and stale-authority denial; `WindowsAppLibraryProvider` only composes it. |
| Launch | No GOG adapter. | No GOG adapter is provided: the fixed registry/info evidence exposes no supported external launch contract, so normalized rows have no Launch capability and Play requests are denied before the provider boundary. |
| Preference UI | Settings owned the ordinary Epic toggle. | The same closed `SettingsPreferencePolicy` and snapshot-only Settings presentation own the independent GOG toggle; the Settings widget root gains no provider or registry dependency. |

Product IDs, registry views/keys, install paths, and info-file bytes remain
inside the trusted provider. Two same-title games
remain distinct by numeric product identity. Malformed, duplicate, missing,
replaced, disabled, unavailable, or stale registrations cannot expose Launch;
one failing GOG source does not disturb another source. No login, credentials,
network request, account-owned catalog, installation/content operation,
user-selected path, external helper, public SDK/protocol change, native work, or
capture work is included.

Focused Release evidence passes Windows app-library provider 75/75,
PlatformSettings 17/17, Settings 58/58, Games & Apps 63/63, and Game Launcher
69/69. A dedicated installed generic-worker/AppContainer route proves disabled
zero-I/O admission, enabled GOG attribution, a disabled **Play unavailable**
projection, and that attempted Play does not cross the broker/provider boundary.
The documentation contract passes across 66 Markdown files.

DLV-153 removes the attempted Galaxy `runGame` adapter after review found no
official GOG material documenting that client command as a supported external
integration contract. `WindowsGogLauncher`, its process invocation, and every
launch-authority claim are removed. The bounded registry/info reader, opaque
identity, source health, cancellation, same-title separation, stale display
behavior, and failure isolation remain; both first-party widgets render the same
provider-agnostic unavailable Play truth.

### Normalized app-library validation ownership correction (DLV-142)

The public normalized app-library shape is unchanged, but SDK validation no
longer lives in one catch-all presentation method. Focused internal validators
now own presentation identity, source reference and source status, availability,
artwork roles/revisions, metadata provenance/timestamps, capability vocabulary,
and operation identity/state. Each can be tested without constructing an
unrelated rich presentation. One narrow composer owns only cross-value rules:
launchability must agree with the explicit `Launch` capability, and an active
operation requires its matching capability, with `Resume` required for a
paused operation. Availability validation separately
requires `Unavailable` and `StaleSource` values to be non-launchable.

Games & Apps and Game Launcher now also state the final admission explicitly:
only a freshly resolved `Installed` item with matching launchability and Launch
capability can reach host launch. Focused fixtures reject unavailable, stale,
retained-last-good, missing-capability, mismatched-operation, unknown enum, and
unknown presentation-version values as `malformed_response` without changing
the normalized DTO, opaque identity, provider projections, or DLV-138/DLV-139
behavior.

Focused Release evidence passes WidgetSdk 89/89, PlatformBroker 56/56,
WidgetBridge 83/83, Windows app-library provider 71/71, Games & Apps 62/62,
Game Launcher 65/65, the installed Games & Apps normalized app-library
acceptance route, and the 65-document contract. No canonical aggregate, native,
network, external-provider, credential, or capture suite was run.

### Game Launcher public SDK portability (DLV-212)

Game Launcher no longer has an SDK `InternalsVisibleTo` exception. The smallest
generic public addition is sanitized launch observation state/result plus
`WidgetAppLibraryService.LaunchObservedAsync`; raw provider and OS identities
remain unavailable. `Export-CommunityReference.ps1` copies the one maintained
managed implementation into a self-contained non-first-party repository created
by the supported `gbar` scaffold, retaining no checkout path, project reference,
friend declaration, or unpublished assembly dependency. The currently bundled
package and tray entry remain unchanged pending the generic native contract and
atomic Community cutover.

Focused Release evidence passes Widget SDK 89/89 and Game Launcher 90/90. The
dedicated CLI path restores from a fresh NuGet cache, builds, validates, packs,
and installs `org.gbar.community.reference.game-launcher` disabled as Community.
The resulting four-file 222,523-byte archive then runs through the ordinary
generic AppContainer worker with declared app-library permissions and exercises
the 10,000-item cursor page, Search, collection navigation, details/Back,
private-state organization, and exact opaque launch revalidation. No native,
catalog cutover, provider, credential, or aggregate behavior changed.

### Spotify responsive player fit correction (DLV-144)

Spotify's pure `SpotifyPresentation` remains the only responsive-composition
owner; `SpotifyWidget` retains lifecycle, provider, and committed-state
ownership without growing. The compact branch now retains the established
controller-first rail and gives its one destination pane all remaining width.
Player uses one bounded vertical Scroll with an intrinsic non-shrinking card,
compact artwork/metadata, seek/times, complete transport, and attribution.
Focus-persistence identities connect the mutually exclusive rail and player
controls while the existing exact actions and route-owned scroll IDs remain.

| Responsibility | Before | After |
| --- | --- | --- |
| Responsive branch composition | Expanded used a rail, while every height-constrained surface switched to horizontal tabs even when the player no longer fit beneath them. | Expanded and compact both preserve the rail hierarchy; compact renders one route in the remaining pane. |
| Player overflow | The compact card was allowed to shrink and clip its transport inside a Scroll whose child no longer had an intrinsic extent. | The compact card keeps a finite intrinsic extent; the one existing vertical Scroll owns overflow and focus reveal. |
| Responsive evidence | Tests asserted that compact/expanded nodes existed. | The focused presenter/style fixture executes the host's 960x540 branch rule over 620x400, 760x440, 978x466, 980x560, and 1280x720 logical bodies at 100%, 125%, and 150% scale, checks selected-branch containment budgets, and proves shared focus identities and seek-to-navigation edges. |

No provider, account, public SDK/protocol, native layout, package, or authority
contract changed. Focused Release evidence passes Spotify 49/49, Widget SDK
89/89, and the 65-file documentation contract. No
Tier-2 production-host run was required because the shared responsive and
scroll contracts did not change.

### Launcher Experience catalog boundary correction (DLV-137)

The Launcher Experience file guard now reads standard lossy `VP8`, lossless
`VP8L`, and extended `VP8X` WebP dimensions from bounded RIFF chunks without a
decoder or runtime dependency. Truncated, malformed, zero-dimension, and
oversized assets fail with stable file diagnostics.

Package discovery stops at 64 files, 64 subdirectories, 32 MiB expanded bytes,
or 16 MiB per file while enumeration is still lazy. Catalog discovery likewise
admits at most 128 ID directories and 128 installed versions before sorting its
bounded result. Duplicate JSON fields retain their exact property path. The
focused PlatformSettings Release fixture passes 17/17, including the named
schema, layout, authority, image, boundary, reparse, catalog, digest, and
built-in recovery cases.

### Launcher Experience production-host geometry proof (DLV-140)

Launcher Experience layout now applies the validated rail orientation to a
host-owned snapshot before both paint and semantic aggregation. The existing
`DeclarativeRenderer` remains the only paint, hit-target, focus-geometry, and
accessibility-geometry producer.

| Responsibility | Before | After |
| --- | --- | --- |
| Responsive layout | `LauncherExperienceLayout` resolved every built-in profile, but only layout rectangles were checked across the matrix. | The same owner resolves every built-in and the reference left rail across compact, standard, wide, 720p, 1080p, taskbar-reserved, and 150%-scale work areas. |
| Slot adaptation | `LauncherExperienceAdapter` rendered host snapshots and canonical semantics, but did not apply recipe rail orientation. | The adapter creates one oriented snapshot used identically by renderer and semantic aggregation; it does not own launcher state or actions. |
| Production evidence | The adapter compiled into `OverlayHost.exe` but only a standalone one-profile WIC test invoked it. | A separate sealed `LauncherExperienceHostProof` owner is dispatched by the production host entry point and verifies the real adapter, pointer, focus, UIA, exact action, and Back route without creating domain or compositor authority. |

Focused Release evidence passes 1,307 native layout/renderer/focus/pointer/UIA
checks. The freshly rebuilt production `OverlayHost.exe` semantic route exits
zero after verifying host-owned launch and Back actions on the same admitted
surface geometry. No capture, pack catalog, Game Launcher domain, window,
composition, or motion behavior changed.

### Launcher Experience encountered-entry bound (DLV-141)

Package traversal now increments the 64-file budget before reparse, path,
extension, duplicate, role, or dictionary admission work. Encountering file 65
stops the current enumerator and leaves queued directories untouched. The
focused Release fixture passes 17/17 and covers all-valid, all-forbidden, mixed,
and repeated-unsafe over-limit sets with a bounded diagnostic count and an
unvisited invalid tail.

### Launcher-scoped presentation and recovery ownership (DLV-133)

Launcher Experience presentation now has one native owner downstream of the
accepted catalog and responsive layout foundation. It merges launcher-only pack
and user slot styles after the existing global computed appearance, applies
accessibility last, decodes sealed static bytes through bounded WIC ownership,
and atomically publishes revision-bound backgrounds and focus effects. No path,
URL, package record, action, or game/provider identity crosses into a worker.

| Responsibility | Before | After |
| --- | --- | --- |
| Catalog and package safety | `LauncherExperienceCatalog` validated immutable recipes, GBSS files, asset headers, identity, digest, and package bounds. | It remains the only package-selection/validation authority; native presentation accepts only an opaque revision and sealed bytes/styles. |
| Responsive layout and semantics | `LauncherExperienceLayout` and `LauncherExperienceAdapter` owned slot bounds, orientation, renderer projection, focus, pointer, and UIA geometry. | Those owners are unchanged; the adapter consumes an optional immutable presentation frame and paints its decoded background through the existing D2D target before rendering the same host-owned slots. |
| Style, artwork, and recovery | No native launcher-only cascade, decode-before-crossfade state, revision quarantine, or safe-start owner existed. | `LauncherExperiencePresentation` exclusively owns pack/user overrides, bounded PNG/JPEG/WebP decode, selected-art revision matching, last-good background retention, effect timing/degradation, accessibility overrides, three-failure revision isolation, and built-in safe-start recovery. |
| Domain/actions/compositor | Fixed host proof content supplied exact action IDs; live Game Launcher projection and composition lifetime were deliberately absent. | They remain absent. Presentation cannot create content/actions, change focus identity, change window/compositor ownership, or project live launcher state. |

Focused Release evidence passes 1,361 native style/asset/recovery/layout/
renderer/focus/pointer/UIA checks. It includes successful real WIC decode for
PNG, JPEG, and standard VP8L WebP; container mismatch, corruption, encoded and
decoded bounds; exact slot scoping; `Use global appearance`; selected-art
last-good retention; accessibility-final overrides; atomic crossfade; and
render/input-budget degradation. The rebuilt production `OverlayHost.exe`
presentation lifecycle route exits zero after switching two decoded revisions,
retaining exact focus/actions, and recovering only the repeatedly failing
revision to the built-in launcher. No aggregate, capture, network, animated
media, audio, managed widget, public protocol, window, or compositor behavior
changed.

### Production computer-control targetability gate (DLV-136)

The bounded product experiment reached the assignment's material-UX stop and
was removed. The accepted main popup (`WS_EX_TOOLWINDOW |
WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST`) is absent from supported
computer-control window/app discovery. A non-tool popup owned by the existing
tool/no-activate backdrop preserved shell exclusion and passed 13 focused
identity/activation/lifecycle checks plus 152 UIA-provider and 34 host-
accessibility checks, but the supported tool still omitted it and Windows
reported the backdrop as the process main window.

On isolated candidate PID 41748, temporarily removing only the owner made the
exact titled main HWND appear in both supported `list_windows()` and
`list_apps()` while `WS_EX_NOREDIRECTIONBITMAP` remained set, proving that
DirectComposition capture identity is not the blocker. The unowned non-tool
popup is eligible for normal taskbar/Alt-Tab shell identity. Adding
`WS_EX_NOACTIVATE` suppressed that shell identity but again removed the surface
from supported discovery and contradicted the required activation/focus path.
No production source or alternate automation surface is retained. A product UX
decision is required to permit taskbar/Alt-Tab presence or a supported control-
tool change is required to admit the existing tool/owned overlay identity.

### Game Launcher native-experience managed boundary (DLV-134)

Game Launcher now owns one bounded persisted built-in experience selection and
one pure semantic-state partition. Hero Rail, Cover Wall, Carousel, and Compact
Grid snapshots contain the same exact action/source-element pairs,
SavedId-derived collection keys, focus IDs, collection anchor, source truth,
and cursor window. The controller-reachable picker persists through the
existing private-state CAS path. An absent or invalid experience selection
falls back to Hero Rail without resetting unrelated organization state.

The managed snapshot marks six host-owned slot roots: details panel, game rail,
collection tabs, source status, operation status, and controller hints. The
projector only moves existing elements and adds non-authorizing wrappers; it
does not add a public protocol field or pack-authored action surface. Focused
Release evidence passes 68/68 Game Launcher tests, including all four profiles,
long-title and missing-art fallback, deterministic repeated projection,
selection persistence/recovery, and the existing 2,000/10,000-item bounded
cursor cases.

The bounded production-shaped conformance group completed 5/6 in 83.1
seconds. Its unchanged installed Game Launcher route still returned 64
provider rows plus the retained manual row as `Unavailable` after 10 app-
library refreshes and two reads, so no launch action was admitted. That
provider/current-resolution limitation is outside this managed projection
change; the failed run is retained and was not repeated.

DLV-145/146 subsequently connected this held managed contract to the ordinary
production host through the private native projection seam. The same admitted
snapshot identities, actions, cursor semantics, and persisted profile remain
managed-owned; no public WidgetProtocol/WidgetSdk expansion was introduced.

### Game Launcher production slot adoption (DLV-145)

The ordinary production host now recognizes only the first-party
`game-launcher-experience` root with one closed profile marker and exactly one
of each six held slot classes. A focused native projection owner extracts those
slots, renders the existing Launcher Experience adapter into one reusable
offscreen D2D target, and commits the complete bitmap only after every slot
succeeds. Missing, duplicate, unknown, or failed projection renders the
unchanged ordinary declarative tree with one bounded diagnostic.

| Responsibility | Before | After |
| --- | --- | --- |
| Private marker recognition and fallback | No production owner consumed the held managed marker; `OverlayApp` always called the ordinary renderer directly. | `LauncherExperienceProjection` exclusively validates the exact first-party widget/root/profile/six-slot shape, selects the existing preset, owns atomic staging and last canonical result, and otherwise delegates to the same ordinary renderer. |
| Layout and paint | `LauncherExperienceAdapter` was exercised only by focused proof code and synthesized a test-only semantic envelope. | The production seam supplies admitted snapshot metadata, focus, viewport, accessibility/options, and work area to the same adapter. The adapter preserves a real Scroll root and changes only its preset orientation while retaining pagination and collection identity. |
| Input, focus, scroll, and UIA | Production pointer/focus/action/scroll/UIA consumers used the admitted ordinary tree and its one render result. | Those same consumers use the canonical snapshot only when it matches the exact widget, instance, sequence, and input scope that produced the current adapter geometry. Exact node/action/focus IDs, collection anchor/keys, sequence, instance, surface, and Back scope remain authored by Game Launcher. |
| Domain, protocol, and presentation lifetime | Game Launcher owned provider, organization, cursor, action, and persisted profile state; the ordinary host owned one renderer/window/compositor. | Those owners are unchanged. Native code adds no game/provider/action state, public schema, protocol field, managed edit, window, compositor, or second renderer. One reusable compatible target is discarded with existing graphics resources. |

Focused Release evidence originally passed 1,540 launcher layout/adapter/renderer/focus/
pointer/UIA checks. It covers all four profiles over compact, standard, wide,
125%-pixel-scale, 150%-text-scale, and nonzero work-area origins; exact root,
instance, sequence, scope, node/action/focus, collection-anchor/item, pagination,
and Scroll identity; shared paint/pointer/focus/UIA bounds; malformed private
shapes and forced adapter failure with one ordinary-path diagnostic; and
semantic-equivalent non-launcher rendering. DLV-146 then replaced the
provider-unavailable-only host proof with a deterministic test-only trusted
app-library backend behind the normal broker and capability path. The ordinary
production host adopted Hero Rail, Cover Wall, Carousel, and Compact Grid with
stable instance/sequence/scope/game identities, then returned to Hero Rail; a
separate unavailable-provider scenario retained the ordinary declarative
fallback. The retained managed 68/68 evidence covers profile selection,
long-title/missing-art states, exact identities, and bounded 2,000/10,000-item
cursor projection. No production bypass was added.

### Launcher Experience production presentation (DLV-148)

The existing `LauncherExperienceProjection` now connects one native
presentation owner to the ordinary adopted Game Launcher path. Each complete
frame atomically carries the selected background, slot paint, focus effects,
pointer geometry, canonical input scope, and UIA semantics for the same
admitted snapshot sequence. Trusted artwork is decoded through the existing
bounded image cache before a background swap; pending or failed artwork retains
the last good background or built-in fallback. Graphics teardown, ordinary
fallback, and hidden/unselected retirement finish presentation transitions
without adding a window, compositor, renderer, timer, protocol field, managed
schema, or provider authority.

The four closed presets project orientation-specific game focus edges onto the
same collection identities. Internal game focus commits before the existing
near-edge collection prefetch action; other widgets retain ordinary pagination
order. Reduced motion removes transforms, reduced transparency uses an opaque
fallback, and render/input pressure deterministically drops effects from full
to opacity-only and then immediate while focus and UIA remain current. The
production diagnostic records preset, sequence, focus, game count, rail edges,
background readiness, transition state, effect quality, input-to-focus p95 and
sample count, and degradation count only when presentation state changes.

Focused Release evidence passes 1,685 native launcher presentation/layout/
renderer/focus/pointer/UIA checks. The isolated ordinary production-host fixture
seeds two trusted games through the normal broker/capability path, adopts all
four presets plus Hero Rail return with stable identities and a distinct
canonical second-game edge, validates decoded artwork readiness and shared
visible focus/UIA bounds, and passes a 60-second actionable-focus stress window
with input-to-focus p95 strictly below 50 ms and deterministic immediate-effect
degradation. A separate provider-unavailable run remains effect-free ordinary
fallback. The named suites ran without aggregate or capture evidence.

### Launcher Experience deterministic authoring toolchain (DLV-135)

`gbar launcher-theme` now owns one focused data-only command surface for
`new`, `validate`, `preview`, `pack`, `inspect`, `install`, `list`, and
`remove`. `CliApplication` retains dispatch/error ownership; the command owner
handles author workflow and deterministic preview composition; and the
`LauncherExperienceCatalog` assembly owns archive capture, package safety,
content validation, digest, immutable installation, exact removal, and the
catalog mutation lock. No validation schema or validator was copied into the
Gbar root; its starter document is accepted only after production validation.

| Responsibility | Before DLV-135 | After DLV-135 |
| --- | --- | --- |
| CLI root | No Launcher Experience command. | One dispatch/error mapping; no package or preview state. |
| Author orchestration | Authors hand-authored directories with no supported command owner. | Focused `LauncherTheme*Command` owners handle scaffold, preview, and catalog workflow. |
| Package/catalog boundary | Production validator admitted expanded directories; catalog discovered and loaded them. | `LauncherExperienceArchive` captures bounded archive bytes, reuses that validator, and owns deterministic pack plus locked immutable install/remove. |
| Native rendering | Existing offscreen adapter fixture only. | Unchanged; ordinary-overlay adoption remains DLV-145. |

Scaffolding publishes a complete valid project atomically. Packing uses ordinal
entries and fixed ZIP metadata, and two independent identical projects produce
byte-identical `.gbarlauncher` archives and digests. Archive inspection captures
bounded bytes and materializes them privately through the existing production
validator. Local installation grants presentation data only, never actions,
SavedIds, provider bindings, paths, game authority, or content authority.

Preview emits a deterministic 132-frame data contract: eleven fixture states
across all four accepted presets and compact/standard/wide surfaces. It covers
empty, 20-game, 2,000-game, offline, long-title, missing-art, active-operation,
150%-scale, reduced-motion, reduced-transparency, and high-contrast states; the
large fixture retains only 64 rows. This is offscreen authoring evidence, not a
claim that the ordinary overlay applies the pack. Production adoption remains
owned by platform DLV-145.

Focused Release evidence passes 61/61 Gbar CLI cases, 17/17 Platform
Settings/catalog cases, the existing native offscreen Launcher Experience
fixture's 1,361 checks, and the documentation contract across 66 Markdown
files. No canonical aggregate is required.

### Cold dashboard first-commit anchor (DLV-143)

The existing DirectComposition placement owner now bottom-aligns every
presented extent inside the retained shared host. A cold compact dashboard is
therefore committed at its final work-area edge before the HWND becomes
visible; widget admission, hide, and resident re-show keep the same coordinate
contract without another HWND, renderer, or widget-specific policy.

| Responsibility | Before | After |
| --- | --- | --- |
| Work-area and host placement | `ShowOverlay` alone selected the live monitor, `rcWork`, DPI/interface scale, compact content placement, and fixed shared-host placement. | That ownership is unchanged; the shared host remains fitted and bottom-centered inside live `rcWork`. |
| Composition mapping | `PlanCompositionMotion` centered both axes, so the 236-pixel cold dashboard occupied the middle of a 919-pixel host even though both placements independently had the correct bottom margin. | The same motion owner accepts an explicit vertical anchor. Production settled and transitional presentations use `Bottom`; X remains centered and full-height widgets naturally retain zero Y offset. |
| Paint, pointer, and UIA | Paint used the visual presentation while pointer inversion and UIA projection consumed the current composition plan, inheriting its centered Y offset. | All three consume the same bottom-anchored scale/offset. No secondary geometry policy was added to `OverlayApp`. |
| Temporal evidence | The first commit log omitted absolute visible-content geometry, so a correct host rectangle could conceal a misplaced compact frame. | Each composition placement records absolute host and visible-content bounds, `anchor=bottom`, and first-visible state. A fresh PMv2 production-host fixture observes that record before exercising dashboard UIA/pointer, widget admission, hide, and resident re-show. |

Focused Release evidence passes 111,381 placement checks across compact, 720p,
1080p, 1440p, 125% DPI, 150% interface scale, taskbar-reserved, and non-primary
profiles; 66 composition-targeting checks; 283 tray-layout checks; and 34 host-
accessibility checks. The fresh production fixture passes on the 120-DPI host:
the first record reports `host-bounds=1785,481,1549,919` and
`visible-content-bounds=1785,1164,1549,236`, with matching physical HWND,
work-area, title, selected tray UIA, and pointer bounds. It then admits the
focused widget, hides it through the production host toggle, and re-shows the
resident owner with focused UIA contained by the committed surface. No
aggregate, capture, managed widget, window-identity, or compositor-owner change
was made.
## Next vertical slices

1. Continue packaged GBA-036 through GBA-042 plus physical mixed-DPI/
   accessibility/visual-regression evidence, including long/error states, the
   150% text-scale matrix, controller focus/Scroll reachability, a denied-
   activation controller lease, Settings permission copy, and the uncaptured
   error/long/max-page states. The auth-free
   `final-schema-v2-20260808-final` bundle records retained package
   archives, AppContainer snapshots, computed styles, 12 standalone widget-
   body PNGs, traces, source/toolchain provenance, and an exact SHA-256 file
   inventory for the covered GBA-038/GBA-042 paths. It includes Spotify 0.1.6
   and the accepted setup/button layout. Settings generic-worker activation
   remains an explicit gap. Live Spotify authorization, callback behavior,
   shell/window composition, and hardware input are separate evidence gates,
   so GBA-038 and GBA-042 remain Verifying.
2. Complete the remaining real-companion and packaged physical-controller/
   shell proof for YT Music. The clean isolated auth-free workflow already
   proves install/consent/AppContainer launch, dashboard/open routing,
   lifecycle/crash/force-reload recovery, content-bound update/rollback, and
   disabled-only uninstall without a trusted fallback.
3. Complete hands-on packaged visual/accessibility/controller evidence for the
   public component milestone. The full Release gate is green. `SettingsRow`,
   bounded nested `ActionSheet`, Picker,
   Scrubber, Toast, rich tiles, protocol-v8 ResponsiveGrid, per-edge borders,
   and CodeText are implemented; Settings, Spotify, and Games & Apps provide
   production uses. After that gate, design advanced/virtualized collections,
   optional packaged-font brokering, and further motion polish without
   importing browser layout or arbitrary asset loading.
4. Extend Games & Apps beyond its bounded Start Menu, AppsFolder, and Steam
   sources with additional reviewed launcher adapters and a host-owned file
   picker while preserving
   opaque exact launch identities.
5. Extend the bounded process sampler with ETW/PresentMon automation, stored
   comparable baselines, latency scenarios, and per-widget resource diagnostics.
6. Add native graphical theme preview, package update discovery,
   editor schemas, controller/focus inspection, and scenario-based Gallery
   preview/capture coverage. The public SDK Gallery is implemented. Complete
   packaged author-workflow evidence for the implemented `gbar dev` loop.
7. Implement publisher signing/revocation, crash quarantine, CPU and disk/
   profile quotas/cleanup, and security audit UI before public community
   distribution; migrate trusted built-ins as their desktop dependencies become
   brokered.
8. Advance the controller-first Audio Control and Network Control roadmap from
   their bounded reference slices through hardware/privacy/performance gates,
   then continue non-auth Performance, richer media, recent games detection,
   and capture references.
9. In parallel when an allowlisted Spotify Development Mode account is
   available, prove the already-composed PKCE/Web API provider and packaged
   Player Community addon against live login/playback. Then add nested device,
   queue, search, recent, library, playlist, album, and artist surfaces. Keep
   the Web Playback SDK local-audio host as a separate later security and
   performance slice.
10. Run the documented controller/game/presentation/anti-cheat matrix.
### Game Launcher scoped action sheet (DLV-151)

The focused game's Y shortcut now opens one bounded SDK `ActionSheet` scope.
Its immutable projection reuses the existing details and organization state for
current favorite, hide, variant, preferred-variant, and source-refresh actions.
B returns to the existing Library or details owner, while View remains the sole
full-details route. The sheet adds no launch, content, or provider authority.

Focused Release coverage passes Game Launcher 70/70.

### Game Launcher exact categories (DLV-154)

Game Launcher private organization now contains a bounded category slice: four
opaque local category IDs, normalized unique names of at most 32 characters,
four members per category, eight exact SavedId memberships total, and an exact
64 KiB serialized-state admission check. Invalid or aggregate-over-budget
category fields reset atomically without clearing unrelated organization state.

Responsibility changed as follows:

| Concern | Before | After |
|---|---|---|
| Category validation/mutation | No owner. | `GameLauncherCategoryPolicy` alone validates bounds/names/identities and owns create, rename, delete, and exact membership mutations. |
| Category projection | No category route. | Game Launcher presentation projects All Games, management, and one exact-member category view from immutable organization/current-window state. |
| Lifecycle, effects, and persistence | `GameLauncherWidget` owned one lifecycle, cursor, navigation, and two-attempt CAS adapter. | The same root remains the sole lifecycle/effect/committed-state adapter and invokes category policy through the existing CAS owner. |
| Launch authority | Every tile required fresh exact SavedId resolution. | Unchanged; missing category members are display-only and a current category tile reuses the same exact resolution/launch path. |

Focused Release evidence passes Game Launcher 73/73. One ordinary generic
AppContainer worker route creates and assigns a category, restarts the worker,
browses the retained exact member, and observes one exact launch revalidation.

### Scalable categories and direct switching (DLV-156)

Category admission now uses the existing exact serialized 64-KiB private-state
boundary before generous validation-work ceilings. The former four-category,
four-member, and eight-total prototype caps are removed; focused state evidence
retains 32 categories and 256 compact exact SavedId memberships and rejects an
over-budget mutation without changing the last committed organization.

`GameLauncherCategoryPolicy` also owns the immutable ordered collection cycle.
The existing widget navigation/query adapter applies that decision: LT/RT wrap
through All Games and category order, preserve the focused exact member when it
exists, and fall back to the first member or an explicit empty-category action.
Search TextEntry, category management, details, and the action sheet expose no
collection-switch shortcut. No provider, launch authority, SDK/protocol, native,
or held Launcher Experience state changed.

Focused Release evidence passes Game Launcher 75/75. The ordinary generic
AppContainer worker route creates and assigns a category, restarts, cycles
RT to the retained category, LT to All Games, RT back to the category, and
observes one exact launch revalidation.

### Exact per-game title override (DLV-157)

Game Launcher now exposes **Edit title** from the existing Y action sheet. A
separate scoped TextEntry stores one normalized, 96-character-bounded title by
exact SavedId; **Reset title** removes that mapping. `GameLauncherTitlePolicy`
alone validates, normalizes, applies, searches, and produces immutable render
projection while the existing widget/state-store adapter remains the sole
lifecycle, provider-effect, and two-attempt CAS owner.

Provider titles remain separately retained in private display recovery. Custom
titles affect current/warm/category/details/hero presentation and search only.
Override-only search performs one bounded exact-SavedId resolve; launch still
uses current AppId/SavedId capability evidence and never consumes title text.
Malformed title fields reset without clearing favorites/categories, a rejected
byte-budget mutation preserves committed state, and CAS replay reapplies only
the requested SavedId override.

Focused Release evidence passes Game Launcher 77/77. The ordinary generic
AppContainer worker route edits a title, restarts, renders and exactly launches
the retained override, resets it, and restores the provider title.

### Trusted Launcher Experience selection and Settings management (DLV-149)

PlatformSettings now owns one additive exact Launcher Experience selection:
global-appearance mode, selected ID/version, and last-known-good ID/version.
The focused mutation policy revalidates exact immutable catalog entries under
the catalog's existing mutation lock. It selects only valid versions, restores
the built-in Hero Rail in one action, and denies Settings-managed removal of
the selected exact version even while global appearance is active.

Settings projects catalog state through a separate snapshot-only presenter. It
shows exact identity, full content digest, claimed publisher, layout preset,
bounded package/image counts, validation state, and built-in versus unsigned
local trust. Controller-scoped list, details, recovery, and removal-confirmation
routes preserve the existing widget lifecycle and committed-state owner. No
public widget protocol, native host, remote gallery, update, signing, provider,
or content authority changed. The commit remains held until its private native
consumer is accepted.

Focused Release evidence passes PlatformSettings 18/18 and Settings 59/59.

### Installed Launcher Experience activation and recovery (DLV-150)

The ordinary native host now consumes the exact trusted PlatformSettings
selection through one private WidgetBridge boundary. The managed boundary loads
only the accepted immutable catalog ID/version, resolves launcher-scoped GBSS,
and publishes the recipe, closed presentation parameters, and optional sealed
static art as one complete revision. The native projection validates every
representative responsive profile and decodes sealed art before replacing its
last good selection; paths, URLs, actions, provider identity, and public widget
protocol remain outside that boundary.

Catalog and settings watchers coalesce valid exact replacement into one atomic
revision. Missing, invalid, removed, tampered, or native-incompatible reload
retains the prior admitted presentation and emits one sanitized bounded
diagnostic per failure transition. Built-in Hero Rail remains a complete
controller recovery. Holding LT+RT while pressing A on the selected Game
Launcher tray item bypasses the custom pack for that activation only and does
not mutate the persisted exact selection. The existing single window,
compositor, renderer, focus/action/UIA identities, accessibility finality, and
presentation degradation owner are unchanged.

Focused Release evidence passes PlatformSettings/catalog 18/18 and native
Launcher Experience 1,691 checks. The ordinary production-host fixture admits
an exact local pack with recipe, GBSS, and sealed art; switches global
appearance; atomically replaces the exact version; retains it after removal
with one diagnostic; and activates built-in recovery. Its inherited DLV-148
motion window remains the separate timing/degradation assertion.

### Byte-budget-driven title override capacity (DLV-159)

The prototype 32-title ceiling is removed. Exact-SavedId title overrides and
their retained sanitized display rows now use generous 1,024-entry validation
work ceilings, while the existing encoded 64-KiB private-state limit remains the
actual admission boundary. Search resolution and unavailable-row presentation
remain capped to the existing 64-item app-library service batch.

Focused state evidence retains 257 compact exact titles below 64 KiB and proves
that a later over-budget mutation rejects without changing the committed bytes.
Malformed, duplicate, and missing-display title fields reset only the title
slice while preserving favorites, hidden/recent/manual rows, variants, and
categories. Rename/reset/restart/search/category/CAS/exact-launch behavior stays
under the same `GameLauncherTitlePolicy` and widget effect owners.

Focused Release evidence passes Game Launcher 77/77 and documentation 66 files.
The ordinary generic AppContainer route also passes after retaining 257 exact
title overrides through a fresh worker session under the 64-KiB boundary.

### Custom Launcher Experience production matrix (DLV-152)

Selection admission now resolves every candidate at compact, standard, and wide
logical extents with both 100% and 150% text scale before one immutable revision
can replace the last good presentation. The code-owned recovery recipes carry
complete responsive branches under the same matrix. Production projection logs
the selected branch, rail orientation, details surface, text scale, reduced
motion/transparency, and high-contrast finality without adding another renderer,
semantic owner, or public protocol field.

The ordinary production-host fixture installs a bottom-rail package and a
left-rail/glass package through the normal catalog, settings, bridge, and trusted
app-library capability path. It proves exact version adoption, long-title UIA
containment, missing selected-game artwork fallback, decoded package artwork,
selected-version removal denial, 150% and accessibility finality, invalid-pack
last-good retention, and exact built-in recovery. High contrast deliberately
removes package art after normal-mode readiness is established.

Focused Release evidence passes PlatformSettings/catalog 18/18, native Launcher
Experience 1,691 checks, and the production-host selection, safe-start, custom
matrix, provider-fallback, and 60-second motion scenarios. The temporal window
records p95 input-to-focus latency of 16 ms with 2 degraded frames.

### Exact Game Launcher launch availability (DLV-165)

The managed broker pipe now retains a bounded set of explicitly canceled
request correlations so one late `request_canceled` response cannot invalidate
the channel used by the replacement app-library request. Unknown and duplicate
correlations still fail closed, and the retained cancellation set is capped at
128 entries and cleared with the client lifetime.

The deterministic broker regression pauses the first canceled response, admits
a replacement request on the same channel, releases the late response, and
proves both the replacement and a later request succeed. Focused Release
evidence passes Platform Broker 56/56 and Game Launcher 77/77. The exact
 installed AppContainer route passes after Hide, Restore, Back, current provider
 resolution, and exact launch revalidation, with no temporary diagnostics in the
 committed route.

### Games & Apps persisted-library audit (DLV-166)

No managed product gap reproduced. The focused matrix passes saved-first warm
projection, delayed and failed background reconciliation, exact removal,
provider refresh, bounded CAS replay, lifecycle reactivation, and fresh-worker
state recovery while preserving unrelated rows and exactly one **Add
applications** action.

The ordinary installed acceptance now reuses one simulated private-state
backend across two real AppContainer worker generations. It adds an exact
Application, stops the first worker, and proves the fresh worker renders the
saved row plus the unrelated trusted Game before completing normal lifecycle
teardown. Focused Release evidence passes Games & Apps 63/63, Windows App
Library Provider 75/75, and the installed restart route.

### Settings installed-catalog quota recovery (DLV-167)

An `installed_widget_version_limit` fault no longer collapses a previously
validated Installed widgets inventory to an empty recovery page. Settings keeps
the immutable last-good rows visible for review, labels them stale, disables
package mutations, and exposes one **Retry installed catalog** action. A cold
start without last-good inventory retains the existing bounded inactive-version
recovery view; no quota is raised and no package is deleted automatically.

The ordinary filesystem-backed Settings route creates eight valid versions,
admits the active widget, introduces a ninth version, and proves the typed quota
code, retained active row, read-only details, unrelated appearance settings,
reactivation, clean restart after catalog change, and explicit Retry recovery.
Focused Release evidence passes Settings 60/60, Widget Catalog 35/35, and the
documentation contract over 66 Markdown files.

### Spotify continuous-list composition audit (DLV-169)

The credential-free Queue, Playlists, and playlist-detail projections now own
explicit adjacent focus edges over their immutable retained rows. Forward and
reverse controller traversal reaches every occurrence exactly once; terminal
input remains on the terminal row instead of wrapping to prior content or page
chrome. The shared bounded cursor resource, repeated-occurrence identity,
12/12/5 paging, eviction/refetch, sparse final page, retry, refresh, and
lifecycle contracts remain unchanged.

Focused Release evidence passes Spotify 50/50 and Widget SDK 89/89. The
isolated Community-package recovery route also launches the current Spotify and
YT Music packages in AppContainer workers and leaves the real user catalog
unchanged.

### Games & Apps artwork availability audit (DLV-170)

No managed product gap reproduced. Trusted Steam artwork and explicit
Application icons remain lazy provider demands, cross the broker only as opaque
generation-bound handles, and retain bounded decoded caches. Games & Apps
projects current handles into the existing AppTile, shows a semantic Play-glyph
fallback while saved-first rows await authority, restores current handles after
refresh and worker restart, and leaves a current row enabled when artwork is
absent.

Focused Release evidence passes Windows App Library Provider 75/75 and Games &
Apps 64/64. The dedicated installed generic-AppContainer Games & Apps normalized
app-library acceptance also passes through two worker generations.

### Game Launcher controls and continuation audit (DLV-171)

No managed product gap reproduced. The existing credential-free 32-game
projection admits Add games, Add running app, Experiences, filters, and current
focused-game View/X/Y/LB/RB shortcuts through one exact action each. Controller
help labels match those admitted actions and disappear while the focused game is
busy. Full, partial, and final cursor pages retain valid collection anchors;
last-row Down advances once to the adjacent entering game while more results
exist, and a terminal page exposes no continuation loop or footer substitution.

Focused Release evidence passes Game Launcher 77/77. The dedicated installed
generic-AppContainer exact-launch acceptance also passes with current SavedId
resolution and launch revalidation.

### Production TextEntry cancel and focus evidence (DLV-180)

The native accessibility tree now projects an authored `TextEntry` as the same
single stable actionable control used by controller A. UI Automation Focus and
Invoke retain exact widget/runtime/snapshot/scope/action admission and open the
existing host-owned modal; no managed widget or public protocol surface was
added. Modal results distinguish failed open, cancel, window close, and commit.
The host records only the sanitized outcome, whether an action was dispatched,
whether the committed snapshot value remained unchanged, and the restored
focus identity.

Focused Release evidence passes `TextEntryModalTests`, `AccessibilityTreeTests`
(17 checks), and the installed generic-worker `LauncherExperienceHostTests`
TextEntry route. That production route focuses and invokes
`widget:game-launcher.search`, types an uncommitted value into the native edit,
cancels through Escape (the keyboard/controller-B modal path), observes exactly
one cancel with `action-dispatched=false` and `committed-value=preserved`, sends
no additional app-library query, and restores exact UIA focus to
`widget:game-launcher.search`.

### Tray UIA Invoke completion evidence (DLV-168)

Host-shell and tray UI Automation providers now use host-owned identity instead
of inheriting the currently selected widget runtime generation. Widget-authored
providers remain generation-bound and therefore still fail closed when stale.
An enabled tray Invoke can consequently complete after its one admitted widget
selection changes the runtime tree, returning success instead of a secondary
COM error; rejected admission remains truthful failure. Pointer and controller
selection paths are unchanged.

Focused Release evidence passes `AccessibilityProviderTests` (155 checks),
`HostAccessibilityTests` (34 checks), `OverlayStateTests`, and the installed
generic-worker `LauncherExperienceHostTests` tray route. That real HWND/UIA route
invokes Network Controls from Game Launcher, observes selected UIA state and
exactly one `game-launcher` to `network-controls` presentation transition, and
receives successful Invoke completion.

### Game Launcher M1 organization and details (DLV-172 through DLV-190)

The Library now exposes bounded controller-reachable **All installed**,
**Continue**, **Favorites**, **Manual**, and non-empty row-proven source
collections. One immutable selection owns the active collection, so legacy
actions select or clear an exact mode instead of composing hidden filters.
Source display labels persist through the existing bounded private-state/CAS
owner, survive paging and cold restart, grant no provider or launch authority,
and are retired only by authoritative unfiltered evidence. Observation-only
empty sources are never advertised.

View projects normalized title, source, typed availability, primary action,
favorite/category/variant state, and bounded optional metadata. Timestamps
outside `DateTimeOffset`'s renderable range are omitted safely. Nested details
and action-sheet scopes consume Back before returning to the exact originating
collection, page, anchor, tile, and focus; collection shortcuts do not leak.
Search remains the one host-owned TextEntry in compact, standard, and wide
semantic trees. Cancel cannot replace committed text, and Clear removes only
search/sort criteria while retaining the selected collection.

Loading, genuine empty, offline library, permission denial, degraded source,
unavailable game, disabled source, active-operation busy, and Retry states have
distinct visible and accessible copy. Disabled, busy, and owned-but-not-
installed tiles remain controller-navigable without gaining launch authority.
**Install from source** appears only for a current typed Install capability and
is informational because no app-library install operation exists. Cursor errors
retain bounded last-good games and exact focus; only the current retry may
replace that state.

Launch uses serialized latest-wins admission and fresh exact-SavedId resolution.
Only current Installed plus Launch evidence can cross the broker boundary.
`GameLauncherLaunchPersistenceCoordinator` owns launch generation admission,
invalidation, accepted-observation Recent transition, stale-write detection,
and restoration through the existing CAS state callback. A newer accepted Play
therefore supersedes a cancellation-ignoring predecessor even when the older
operation is already inside private-state persistence. Rejected or inactive
admissions do not reserve generations. The root remains the sole lifecycle,
operation, private-state I/O, committed-state, and render owner and shrinks
rather than accumulating another coordination region.

Focused Release evidence passes Game Launcher 90/90. Installed credential-free
AppContainer routes cover organization and later-page source persistence,
source removal and empty-source exclusion, mixed installed/owned presentation,
exact launch denial/admission, TextEntry scope restoration, availability and
last-good recovery, exact offline revalidation, stale/missing denial, retry
focus, one deduplicated Recent sequence, and the cancellation-ignoring blocked-
write ordering. YT Music 59/59 and Now Playing 27/27 also pass their corrected
Active-generation and stale-command failure cases; the existing installed
Community/media-session recovery routes remain green. Documentation validation
passes over 66 Markdown files.

### YT Music transport and recovery outcome (DLV-173/DLV-176)

YT Music distinguishes connected idle from current playing or paused media.
Connected idle exposes only focused Refresh and no stale dashboard transport.
A transient poll or explicit-refresh failure preserves last-good media with
truthful stale status, while a later success replaces it atomically.

Explicit Refresh runs in its own Active-generation latest-operation context.
Success, sanitized ordinary failure, and authorization failure all require that
exact context at final state commit. Cancellation-ignoring ordinary and auth
failures from an earlier Active generation cannot alter later presentation or
credential authority. Focused Release evidence passes YT Music 59/59 and the
installed Community fake-companion recovery route.

### Now Playing recovery and quick actions (DLV-174/DLV-177)

Identity-less or duplicate session generations are invalid provider data rather
than an authoritative empty result, so the current session, metadata, supported
quick actions, and bounded recovery status remain visible. A genuine empty
snapshot still renders **Nothing is playing**.

Command success or failure can reconcile only while its Active generation,
snapshot revision, admitted session, and selected session remain exact. A stale
failure returns the current immutable replacement state wholesale instead of
applying rollback status. Focused Release evidence passes Now Playing 27/27,
Platform Broker 56/56, and the installed media-session lifecycle/retry route.
### Author-to-production Launcher Experience lifecycle (DLV-160)

The supported CLI scaffold now emits bottom-rail compact, standard, and wide
geometry that passes the production native compatibility matrix at 100% and
150% text scale. One isolated conditional Gbar CLI test owns the complete
author path: scaffold and validate bottom-rail v1 plus left-rail/glass v2,
generate path-free previews, pack, inspect, install into the real catalog, and
select through `LauncherExperienceSelectionPolicy`.

The same catalog and Settings root then enter one ordinary production host and
private bridge. That single HWND/compositor/renderer first bypasses v1 for one
safe-start activation, restores v1, atomically adopts exact v2, retains v2 when
its recipe reload is temporarily corrupted, and activates built-in Hero Rail.
After valid bytes are restored, the supported CLI removes both now-unselected
custom versions; exact Settings selection remains Hero Rail and catalog listing
contains no stale custom version. Retained preview, archive, native output, and
host-log evidence contains no local path or remote/executable content.

Focused Release evidence passes 1,691 native Launcher Experience checks and the
one exact author-to-production lifecycle route. No schema, public protocol,
network/gallery/signing surface, renderer, window, or presentation owner was
added.

### Current widget-switch continuity verdict (DLV-188)

The current single-window DirectComposition path does not reproduce the reported
black border, flicker, or tray motion under the bounded production-shaped
eight-widget cycle. No product code changed. The existing real-HWND fixture
retains source content during delayed worker startup, commits each complete
destination surface before coordinated placement, keeps the shared shell/tray
stationary, and uses premultiplied-clear composition with no HWND class
background. It also rejects direct-HWND fallback, render-target recreation,
stale composition work after hide, and out-of-work-area placement.

Focused Release evidence passes 111,381 placement, 66 targeting, 63 transition,
45 chrome, 155 accessibility-provider, 49 focus, 34 host-accessibility, and
4,839 renderer checks. The real-host cycle passes all eight identities with
maximum draw 3,298 us, commit 1,190 us, coordinated geometry 1,303 us, and zero
shell-motion commit time. This semantic/timing/log result does not replace the
separate user physical-display verdict.

### Full application-scale public SDK reference (DLV-195)

The optional Full Application sample keeps a deterministic 10,000-record model
inside its isolated worker and projects only 32-record cursor pages with a
96-record retained host window. It uses only public `WidgetCursorResource<T>`
and `WidgetNavigator<T>` surfaces for bounded virtualization, Library/Details
navigation, exact Back focus, safe failure/retry, refresh, and Active-lifetime
cancellation and drain. It requests no capabilities and is deliberately absent
from the built-in tray catalog.

Focused Release evidence passes the four-case MSTest.Sdk reference suite and
the dedicated generic AppContainer package route. The latter validates and
installs the checked-in manifest and assembly into an isolated catalog, renders
the 10,000-record summary through the ordinary worker, and leaves the worker
contained and running after its first valid snapshot.

DLV-201 makes the reference's transient Library presentation valid as well as
bounded: NotLoaded and Loading publish no initial focus because they render no
focusable control, Error retains exact Retry focus, and Ready retains requested
or item focus (falling back to the rendered Refresh control for an empty page).
The four focused tests validate blocked Loading and post-deactivation reset
snapshots through the protocol validator, and the generic AppContainer route
passes unchanged.

DLV-196 exercises that reference pattern from a clean temporary Git-repository
shape using the copied DLV-194 `gbar` distribution. A fresh local NuGet cache
restores only from the generated cleared feed; the external project then builds,
validates, packages, installs into an isolated catalog, runs its deterministic
scenario in the AppContainer preview worker, and uninstalls every isolated
package version. The consumer and archive contain no checkout-relative path,
and the route performs no external network, publication, signing, credential,
or production-catalog operation.

DLV-202 replaces that fixture's undocumented checkout copying, scaffold-source
deletion, manifest replacement, and scenario authoring with the same bounded
`Export-ExternalReference.ps1` command documented for developers. The exporter
uses the copied `gbar` release unit to create the offline SDK scaffold and writes
the complete application-scale source, manifest, styles, and scenario before the
external repository runs the documented restore/build/validate/pack/install/
scenario/remove sequence. The exact onboarding case passes 1/1 and the
documentation contract passes over 68 Markdown files.

DLV-198 records a bounded executable compatibility report for the current
versioned SDK/gbar/template release unit and a planner-ready migration and
deprecation proposal. DLV-203 refreshes that report from exact corrected DLV-202
commit `5fbf690bcfa8f20cb4eaff8012a6c7df17a0817f`: the rebuilt SDK and CLI share
that product-version provenance, the reviewed API contains 2,997 symbols, SDK
SHA-256 is `2392741C815C604BA29CFEABF6843EF3033907E4CC5901774E0D4C6B5AD26193`,
and gbar SHA-256 is
`E081DE4D0FF1D018BA3422074F68168F046B8528FBFAB950581195643BE70981`.
The focused release-unit run passes WidgetSdk 89/89 and compatibility 12/12.
These milestones change no API baseline, protocol, template, version, resolver,
publication, signing, or compatibility policy.

DLV-204 proves the shortest supported presentation-only author loop in the same
self-contained external repository. One documented heading edit is followed by
Release build, validation, declaration preview, isolated scenario execution, and
packaging; the fixture requires the edited heading in the scenario result before
accepting the archive. It reuses the restored offline SDK and isolated CLI paths
and adds no install, publication, signing, account, network, native, or renderer
behavior.

### Generic Community advanced presentation contract (DLV-213)

The package manifest now has one optional version-1 `launcherExperience`
declaration. Protocol v16 adds one closed preset value per immutable view and
six typed semantic slot roles. The Widget SDK exposes those values directly;
package identity, publisher, assembly/type, element IDs, style classes, and a
memorized root shape are not part of native admission.

The bridge copies the declaration only from a validated bundled or installed
manifest, includes it in the public descriptor and presentation-generation
fingerprint, and gives arbitrary Community identities the same boundary. The
native projection locates exactly one non-nested container per closed role,
uses the existing Launcher Experience adapter/presentation owner and single
renderer/window/compositor, and retains canonical interaction geometry only
for the exact current descriptor generation. Missing declarations, malformed
slots, incompatible schema/preset, replacement generations, or adapter failure
fall back atomically to the current ordinary declarative tree. Actions, focus,
collection keys, active scope, Back, pointer geometry, and UIA remain authored.
Launcher Experience Packs stay data-only and Settings/host-owned.

The adjacent internal tray gesture state/action names now say `Restart` rather
than `Refresh`; its 700 ms timing, Y tap/release/cancellation behavior, generic
worker-restart authority, help text, and public protocol are unchanged.

The maintained Game Launcher projection and its bundled and supported
`Export-CommunityReference` manifests now consume protocol v16 with the typed
declaration, preset, and all six slot roles. The retired private profile/slot
style markers are absent. The exact exported candidate, not a lookalike test
package, is installed beside an unrelated differently named and shaped fixture
through the ordinary package route. The production host admits both through the
generic contract, preserves their authored action/focus/collection/Back/UIA
semantics, and also admits the currently packaged Game Launcher without an
advanced-presentation fallback.

Focused corrected Release evidence passes Widget SDK 91/91, Widget SDK
compatibility 12/12, installed bridge declaration 1/1, native bridge catalog
parsing, Game Launcher 90/90, the Full Application Community reference 4/4,
the exact CLI export case, verifier self-test, 1,695 native
projection/layout/render/focus/UIA checks, and the three-candidate ordinary-host
run described above. The `0b756df` clean-commit aggregate attempt failed in the
verification-runner self-test before any product step because
`FullApplicationWidget.Tests` was missing from `verification-steps.json`; that
attempt is not passing evidence. The project is now listed with its bounded
MSTest.Sdk invocation. The corrected aggregate is run once only after this
correction is committed, and its exact result belongs to the milestone report;
no capture evidence is used.

### Generic full-trust Community application runtime (DLV-215)

DLV-215 adds one versioned `full-trust-application-v1` manifest entrypoint for
an exact immutable package executable. Install and enable deny it by default;
CLI and Settings require an explicit full-trust approval while disclosing that
the executable runs as an ordinary current-user process outside AppContainer
with ambient file, network, registry, database, and child-process authority.
There is no sandbox-to-full-trust fallback.

The public author boundary is the new narrow `WidgetApplicationRuntime`
bootstrap plus `WidgetSdk` and `WidgetProtocol`. The author package deliberately
contains neither the host-side `WidgetRuntime` nor `PlatformBroker`, and the
bootstrap creates no capability-broker connection. Host-private launch policy
still pins the selected content generation and exact executable, authenticates
a random session nonce and connected PID, validates bounded protocol messages,
and retains lifecycle, failure/restart, update, disable, removal, and
kill-on-close process-tree ownership. Full-trust sessions intentionally receive
no AppContainer, broker grants, isolation-key ACL projection, private memory
ceiling, or one-process restriction.

Two unrelated installed fixtures use the ordinary catalog and bridge route.
The first starts a child process, performs deterministic current-user file and
transactional JSON-database work, and exercises a fake HTTPS transport; the
second proves the same generic runtime with a different identity and tree.
Focused evidence covers explicit trust denial/approval, exact executable
selection, nonce/PID authentication, malformed/oversized/stale protocol
denial, crash/restart with retained state, replacement generation, drain,
disable, and removal. No package, provider, OAuth, assembly/type, element/style,
or tree-shape special case is present.

Pre-commit focused Release evidence passed Widget SDK 92/92, Widget Catalog
35/35, Widget Runtime 75/75, sandbox WorkerHost 10/10, Widget Bridge 86/86,
gbar CLI 65/65, Settings 61/61, and the native WidgetBridgeCatalog fixture. The
clean exact-commit Tier 3 run for `ed39a70` stopped at Widget Runtime 74/75:
the cancellation-ignoring retired gesture-grant case observed two permitted
idempotent revocations and its invalid `.Single()` assertion failed. The
retained result is
`artifacts/verification/20260813T063409Z-37fcbe6e/verification-result.json`;
it is explicitly not passing aggregate evidence. The copied
external consumer restores only the generated local SDK package, contains no
repository `ProjectReference`, emits only the narrow application runtime plus
SDK/protocol dependencies, and completes its child/file/database/fake-HTTPS
snapshot through the authenticated supervisor. The documentation contract
currently remains red only on five links rooted in the reviewer-owned archived
delivery-plan file `2026-08-12T21-06-00-07-00.md`; no DLV-215 documentation file
is named by that failure.

### Autonomous Spotify full-trust Community application (DLV-216)

Spotify 0.3.0 now enters the product as an ordinary
`full-trust-application-v1` package. The immutable package executable owns PKCE,
the Web API transport and strict parser, package-scoped public configuration,
the existing Credential Manager target, token refresh/rotation, Player, Queue,
Playlists, Devices, local playback policy, and its WebView2 playback child and
protocol. The widget consumes one package-local typed application interface.
Neither the manifest nor payload uses `external.spotify.*`, `PlatformBroker`,
`PlatformSettings`, `WindowsSpotifyProvider`, or another product-specific
Spotify assembly. Product ownership stops at generic package ingestion,
explicit full-trust consent, authenticated overlay IPC, bounded snapshots,
lifecycle/restart, presentation, disable, and removal.

The default configuration path and publisher/package identity are unchanged,
and the package backend uses the same stable Credential Manager target. Existing
Client ID state and refresh credentials therefore remain available without a
migration or deletion step. Provider error bodies are reduced to package-owned
safe messages before reaching the widget. The retired product-core provider and
playback paths remain intact as the frozen fallback required until DLV-218; the
autonomous package does not reference them, and the comprehensive provider and
playback suites now compile against the package-owned source.

Focused Release evidence passes Spotify widget 50/50, package backend 32/32,
credential-free application 5/5, playback client 3/3, playback host/protocol
10/10, gbar CLI 65/65, and ordinary Widget Bridge 87/87. The documentation
contract reaches only unchanged reviewer-owned delivery-plan history links and
remains blocked there; the DLV-216 documentation diff passes `git diff --check`.
The isolated package path emits 23 files / 3,271,094 bytes, requires explicit
full-trust approval, installs/selects/enables 0.3.0, and returns the exact
credential-free setup snapshot through the ordinary installed catalog and
generic full-trust supervisor. No aggregate, account credential, live Premium/
EME session, native renderer, Avalonia, capture, or publication work is claimed.

### Exact-commit full-trust Runtime verification correction (DLV-220)

DLV-220 preserves the accepted DLV-215 runtime architecture and corrects only
the cancellation-ignoring retired gesture-grant fixture. Session retirement may
issue one best-effort revoke while the input request unwinds and a second revoke
after a cancellation-ignoring grant actually completes. Both target the same
input sequence and are intentionally idempotent; the fixture accepts only one
or two observed revocations and requires every observed sequence to be the
exact retired input sequence 92.

Focused Release evidence passes Widget Runtime 75/75 and the packaged ordinary
full-trust lifecycle 1/1. The latter installs and runs two unrelated package
executables, exercises child/file/database/fake-HTTPS work, restart, replacement,
drain, disable, and removal through the normal catalog and supervisor. The one
clean exact-correction-commit Tier 3 result is retained separately and reported
with its exact commit provenance; these focused results are not described as an
aggregate pass.

### Generic Hero Rail terminal-artwork recovery (DLV-210)

The native Launcher Experience projection now derives one closed artwork state
from ordinary opaque cache handles. Hero Rail keeps its existing presentation
for pending, available, and mixed results. After all tracked rail artwork is
terminally unavailable, it atomically selects a code-owned composition that
keeps source/title, Search and collections, one equal-width horizontal game
rail, operation state, and one controller-help hierarchy inside the admitted
body. It does not fabricate artwork or recognize a widget, package, provider,
game, element, or style identity.

The recovery frame changes only presentation copies. The canonical snapshot
retains exact action IDs, collection item keys, anchor, pagination actions,
active scope, and authored focus edges. Terminal artwork nodes use the shared
Play fallback while their oversized presentation boxes compact; the six-card
rail receives equal host-owned widths and remains horizontally revealable.
Production diagnostics now retain the generic artwork state plus exact body and
rail bounds for temporal verification.

Focused Release evidence passes 2,890 native checks across the exact 978x466
compact body plus standard/wide profiles and pending/available/mixed/
all-terminal states. The matrix retains all 32 exact action/SavedId/focus edges
and asserts pointer, focus, and UIA geometry containment. The packaged
`--no-artwork-only` production-host route passes with 32 seeded ordinary catalog
items, six terminal visible handles, equal-width contained cards, reachable
Search/collection/help semantics, stable visible focus identities, and a clean
post-route projection/composition log. The legacy combined host route remains
red before this scenario at its existing compact `experiences.open` expectation;
it is not claimed as DLV-210 passing evidence and was not weakened or removed.

### Native Taffy stretch and stationary variable surfaces (DLV-222)

The native declarative translation now treats GBSS `align` only as a
container's child alignment (`align-items`). An ordinary auto-width Row or Stack
continues to inherit its parent's Taffy stretch even when it centers its own
children; definite dimensions, aspect ratio, and the existing generic bounds
still constrain that node. The direct nested column/centered-row regression
proves a flex-growing slider receives the exact remaining width at both fill and
explicitly constrained widths, without a widget or package identity.

The host again uses the admitted or retained `windowWidthDip/windowHeightDip`
as its presentation extent. Variable content is bottom-centered inside the one
existing DirectComposition/HWND owner, and source/destination extents use one
transparent union container during nonblocking motion. Panel geometry is
bottom-anchored so the visible panel edge, fixed-height controller guide, and
tray retain their ordered offsets. The host-owned tray centers one bounded
560-DIP coordinate band on wider surfaces; the current eight identities and a
bounded ninth catalog addition fit at no less than the existing 44-DIP target,
while genuinely constrained work areas keep the established overflow path.

Focused Release evidence passes the pinned Rust bridge 5/5, DeclarativeLayout
250 checks, DeclarativeRenderer 4,842, OverlayPlacement 112,303, TrayLayout 300,
controller navigation 107, slider interaction 2,086, focus navigation 49,
surface focus 24, and the affected accessibility/UIA layers. Audio Mixer passes
45/45 and Network Controls passes 24/24, including the authored 560x700 surface
and `Ready to scan` semantics. The single linked production-host fixture cycles
all eight identities with stable bottom-centered tray bounds and full capacity,
retained/admitted input and focus authority, work-area containment, transparent
hit testing, normal close/reopen, and clean shutdown. Its maximum draw, commit,
coordinated-geometry, and motion-commit times are respectively 2.999, 1.796,
2.121, and 0.082 ms. The Release host builds without packaging; the visible
repackage/display/controller verdict remains intentionally queued until this
native milestone and the concurrent widget presentation milestone are both
accepted and integrated.

### Responsive YT Music controller composition (DLV-223)

YT Music 0.2.8 keeps one semantic media tree and moves progress plus both
controller rows into the same metadata column as title, artist, and album. At
the 760-DIP preferred width that column sits to the right of the 128-DIP
artwork. At the 480-DIP compact budget, the generic wrapping Row moves the
artwork above a full-width details column inside the existing vertical Scroll.
Action IDs, explicit focus edges, LB/RB/X/Y shortcuts, quick-action authority,
selected/busy states, and artwork fallback are unchanged. No native renderer,
surface-axis protocol, lifecycle, companion, or authentication owner changed.

Focused Release evidence passes YT Music 60/60, the isolated Community
validate/pack/install/consent/AppContainer/update/rollback/remove route, and
4,777 existing generic native renderer checks covering wrapped controller
targets, focus reveal, and bounded geometry. The isolated package evidence is
retained at `artifacts/acceptance/ytmusic-community-addon.json`; the real user
catalog remained read-only. A coherent Release containing both DLV-222 and
DLV-223 still needs the user's physical composition and tray-spacing verdict.

### Symmetric widget surface-axis sizing (DLV-224)

Protocol v17 adds one typed `WidgetSurfaceAxisMode` shared independently by
`WidgetSurfaceHints.WidthMode` and `HeightMode`. Both default to `Preferred` and
are omitted from legacy wire payloads, so existing protocol-v2 views retain
their validated preferred extents and stable live-data behavior. Explicit
`Content` or `FillAvailable` requires v17; malformed, undefined, and
version-incompatible values fail validation or native admission without
replacing the retained presentation.

The one existing placement owner admits work-area, DPI, interface, text-scale,
minimum, and preferred bounds before invoking the one existing renderer for at
most one intrinsic pass. Taffy receives the admitted width independently from
the height policy: Content width stays automatic, while Preferred or
FillAvailable width remains definite during Content-height measurement. The
measured extent is finite and bounded, clamped between authored minimum and
preferred/work-area ceilings, augmented by host chrome, cached per immutable
snapshot and admission constraints, and followed by the ordinary final layout.
No widget, package, page, style, or known-tree identity participates. The one
HWND, renderer, focus graph, GameInput owner, scroll state, accessibility tree,
and fixed bottom-center panel/guide/tray anchor remain unchanged.

Focused Release evidence passes Widget SDK 93/93, SDK compatibility 12/12,
Widget Bridge 89/89, pinned Rust/Taffy 5/5, DeclarativeLayout 250,
DeclarativeRenderer 4,851, OverlayPlacement 112,333, and the affected native
composition/focus/accessibility group. The final native Release builds, and the
bounded managed-snapshot-to-production-host fixture cycles all eight widget
workers with explicit zero-pass FillAvailable and one-pass Content admissions,
normal focus/input ownership, fixed tray geometry, clean shutdown, and maximum
draw/commit/coordinated-geometry/motion-commit times of 3.461/1.700/1.946/0.057
ms. Documentation validation reaches only eight pre-existing broken links in
reviewer-owned delivery-plan history snapshots; no DLV-224 authoring document
is named by that failure. The required canonical checkpoint is intentionally
run exactly once only after this coherent implementation is committed cleanly;
its retained exact-commit result is reported separately and is not preclaimed
here.

### Truthful bounded performance provenance (DLV-206)

The private performance harness now records exact scenario/process identity
instead of describing ephemeral measurement profiles as the ordinary live
host. JSON and Markdown retain root PID/start, scenario/profile, commit,
executable SHA-256, dirty state, observed child roles, and explicit available
and unavailable metrics. The existing Hidden/Visible/Interactive measurement
scope, sampling cadence, diagnostic-only comparisons, and cleanup boundary are
unchanged.

The separate eight-widget production-host fixture now emits its own
PID/start/profile/commit/executable/child-role provenance. Its retained and
admitted temporal checks begin composition lookup after the matching paint line
ends, so an earlier commit cannot satisfy the evidence. Bounded pre-commit
validation passes all 23 harness assertions, a Release native build, the
eight-widget route with eight composition samples and seven switches, and one
Hidden/Visible 21-sample observation. Exact clean-commit timing and resource
values belong to the retained reports and completion record; this status does
not convert unavailable private-working-set, GPU, presentation, or scheduler
data into a claim.

### Self-contained action-failure production-host fixture (DLV-227)

`WidgetActionFailureHostTests` now validates every admitted non-system native
dependency before launch and copies `OverlayPlatformInterop.dll` beside the
temporary `OverlayHost.exe`. A direct regression proves the copy is present and
byte-sized identically and that omission fails before launch with the precise
dependency diagnostic. The temporary installation retains job-owned teardown
and directory cleanup.

The packaged runner removes the Release installation from PATH, and the native
fixture independently rejects a contaminated PATH. A unique private test
process profile prevents delegation to an already-running ordinary owner while
retaining the exact visible-HWND/UIA/action-failure checks. The focused Release
packaged segment passes with an accepted ordinary host still running; no source
worktree fallback, PATH addition, HWND weakening, aggregate run, or product
runtime change is involved.
### Content-sized Settings root (DLV-225)

The Settings root now requests Preferred width and Content height with its
existing 520x360 minimum and 880x520 preferred bounds. Its bounded responsive
category grid has a root-only scroll class that preserves two-column and
one-column reflow without the nested-page `flex-grow`/`flex-basis` fill policy.
Dynamic nested pages remain Preferred on both axes. Category IDs, active input
scope, explicit focus graph, Reset styling, scoped B navigation, and the host's
single surface/tray/guide placement authority are unchanged.

Focused Release evidence passes Settings 61/61 and Widget SDK 93/93. The exact
managed Settings snapshot and compiled production GBSS pass through the
production native bridge parser and Taffy renderer for 44 checks: preferred
content measures to 372 DIPs, compact one-column content grows to 580 DIPs and
is clamped to the authored 520-DIP ceiling, Reset has no material trailing fill,
and every root action is navigation-visible and focus-revealed at 520x360. The
isolated Settings worker Release build succeeds with zero warnings or errors.
The older standalone `OverlayPlacementTests` target is not counted as evidence:
on this accepted baseline it has a pre-existing unresolved
`ComputeOverlayPlacement` link symbol, while the DLV-225 production-renderer
scenario and DLV-224 placement owner remain unchanged.

### Eight-widget surface-policy audit (DLV-226)

The current Settings, Audio Mixer, Network Controls, Games & Apps, Game
Launcher, Now Playing, Spotify, and YT Music first-page surface contracts are
now tabulated in the public authoring guide. The audit retains Preferred width
and height for every provider/list-driven surface so changing sessions,
networks, catalog pages, playback, or remote collections cannot resize the host.
Settings root remains the sole directly supported Content-height adoption;
deeper Settings pages remain Preferred. No widget, protocol, host, focus,
scroll, or placement behavior changed in this milestone.

Focused Release evidence passes Widget SDK 93/93, Settings 61/61, Spotify
50/50, and YT Music 60/60. The older first-party conformance group is retained
honestly at 1/6: its five failures occur before widget admission because the
harness still requires the now-full-trust Spotify package to declare a managed-
worker assembly. Documentation validation likewise reaches only nine existing
broken links in reviewer-owned delivery-plan history snapshots. Neither red
result was weakened or reinterpreted as surface-policy evidence.

### Audio Mixer admitted-width consumption (DLV-228)

Audio Mixer now authors percentage width on its root, cards, device/control
rows, and session list. Master, microphone, and per-session sliders retain one
flexible `flex-grow`/`flex-shrink` track with no obsolete fixed minimum, while
mute icons and percentage values keep their bounded widths. Labels, values,
actions, focus IDs/edges, one whole-widget Scroll, compact reflow, and the
Preferred 520x520 surface policy are unchanged; the native host has no Audio
identity rule.

Focused Release evidence passes Audio Mixer 45/45 and the generic native
renderer's 4,851 checks. A dedicated real managed snapshot plus production
GBSS/bridge/Taffy scenario passes 41 checks at 320- and 520-DIP widths: root,
cards, headings, session list, and control rows consume their exact admitted
inner widths; master, microphone, and session sliders take the exact remainder;
and the focused session remains scroll-revealed.

### Network first-page vertical admission (DLV-229)

Network Controls now declares its symmetric Preferred/Preferred policy
explicitly while retaining the measured 560x700 preferred and 320x420 minimum
envelopes. The provider-driven Wi-Fi/Bluetooth body remains one bounded nested
Scroll, so scans and device churn cannot resize the host. No state is hidden,
no list uses Content sizing, and no Network identity rule was added to native
placement.

Focused Release evidence passes Network Controls 24/24. A real NotScanned
managed snapshot plus production GBSS/bridge/Taffy scenario passes 12 checks:
the 700-DIP authored panel's 644-DIP content viewport shows the complete
`Ready to scan` title/help and primary Scan action at zero body-scroll offset,
while the 320x420 minimum envelope's constrained 364-DIP content viewport
focus-reveals both radio and Scan controls.

### YT Music unified media panel (DLV-230)

YT Music 0.2.9 keeps the accepted single responsive tree and makes its media
layout one full-width raised panel inside the admitted Preferred/Preferred
surface. The root and panel consume the available inner width; artwork remains
beside the unified metadata, progress, and controller column at 760x440, while
the same tree wraps artwork above a full-width details column at 480x340. All
eight actions, explicit focus adjacency, scroll reveal, companion lifecycle,
authentication, and Community isolation remain unchanged. No native identity
rule, host offset, second page tree, or provider behavior was added.

Focused Release evidence passes YT Music 60/60 and a real managed snapshot plus
compiled production GBSS through the production native parser and Taffy
renderer for 82 checks at the preferred and compact budgets. The isolated
validate/pack/install/consent/AppContainer/update/rollback/remove lifecycle
passes for immutable package 0.2.9 with archive SHA-256
`ca334cc1059393ef93c4cf3d68a9b21b17339bbdd9a87118d2181c0faafd9aac`;
the real user catalog remains read-only. Physical composition remains the final
verdict for panel-to-host-chrome cohesion.
### Slow and nonresponsive worker response isolation (DLV-231)

The existing native widget-session coordinator now owns one cancellable token
for its exact in-flight request. Selection-away, hide, invalidation/worker exit,
and shutdown revoke that widget generation, remove its queued work, and cancel
only the coordinator worker thread's synchronous bridge I/O. The one existing
bridge pipe remains authoritative: responses with an older correlation ID are
discarded, while a future/malformed ID still fails closed. Lifecycle
completions must also match the exact current target, so a late Background
acknowledgement cannot erase a rapid reselection. No second event loop,
transport, input router, focus graph, cache, or presentation owner was added.

Focused Release evidence passes 12 coordinator scenarios covering delayed
success with last-good retention, a never-completing request, selection-away,
hide, worker-exit invalidation, shutdown, cancellation-ignoring late success
and failure, and stale generation rejection. Native bridge/catalog correlation
passes, and the managed bridge lifecycle/concurrency fixture passes 89/89. The
packaged eight-widget production-host route blocks two exact Settings render
sequences, revokes one by tray selection and one by ordinary B close, and proves
neither is admitted. It preserves exact sequence/extent/focus through
reselection and admits only a later valid snapshot. Host-focus p95 is 28 ms
across nine samples (22 ms selection-away, 28 ms reselection); B dispatch is
2 ms. The intentionally late worker completes separately in 36 ms after fixture
release. Visible hide completion is recorded separately at 2,024 ms and is not
included in the established 50-ms host-focus target. The production route
retains exact PID/start/profile/commit/executable/child-role provenance and
job-owned zero-process teardown; no aggregate or capture route was run.

### Self-contained Audio Mixer scroll host fixture (DLV-233)

Native production-host fixtures now share one admitted runtime-dependency policy
from `OverlayHostTestSupport`: `OverlayPlatformInterop.dll` is validated before
launch, copied beside the temporary `OverlayHost.exe`, checked for identical
size, and an omitted dependency produces the exact pre-launch diagnostic. The
Audio Mixer temporary installation no longer depends on the source Release
directory or PATH. Its existing fixture worker, authenticated readiness nonce,
visible HWND, UIA, live reverse-edge scroll/focus assertions, evidence commit,
job-owned teardown, and temporary-directory cleanup are unchanged.

The focused Release `AudioMixerScrollHostTestsOnly` packaged route passes while
the admitted Release installation is removed from PATH, including the shared
copy/omission regression and complete Audio host scenario. A precautionary
focused `WidgetActionFailureHostTestsOnly` run compiled against the same shared
policy and passed its dependency setup before retaining a later red functional
assertion, `The primary worker-start failure was not retained by the host.` That
separate worker/session behavior is not weakened, rerun, or changed by this
test-infrastructure correction. No Audio product code, aggregate, capture, or
source-worktree fallback was used.

### Frame-safe slow-worker bridge recovery (DLV-234)

The sole native `WidgetBridgeClient` transport now treats every incomplete
frame header or body read, invalid frame length, and incomplete frame write as
tainting the byte stream. The next request closes that pipe, performs bounded
teardown of the one owned bridge process, and establishes one replacement
through the existing lifecycle owner before sending another frame. Correlation
mismatches fail closed; the former stale-request-ID skipping path was removed
because it could not recover alignment after a partial synchronous read. No
second transport, protocol, session, input, focus, or presentation owner was
added.

Direct native regression coverage cancels a synchronous read with no published
bytes, after two header bytes, and after a complete header plus seven body
bytes. Each case proves the read was pending, cancellation returns
`ERROR_OPERATION_ABORTED`, and the transport is tainted; a complete ordinary
frame then succeeds on a separate replacement pipe. Focused Release evidence
also passes all 12 coordinator scenarios, native bridge/catalog correlation,
and the managed bridge lifecycle/concurrency fixture at 89/89.

The packaged eight-widget production-host route proves selection-away and
ordinary B close each revoke the blocked result, tear down the old bridge, and
resume through exactly one different bridge process, with zero retained
processes after job-owned cleanup. Host-focus p95 is 33 ms across nine samples;
tray navigation is 33 ms and B dispatch is 1 ms. Worker/bridge completion is
reported separately: selection-side replacement completes in 1,033 ms,
visible hide completes in 1,241 ms, and close-side replacement completes in
1,306 ms. Seven admitted transitions retain a 17 ms input-to-retained maximum
and 749 ms input-to-complete maximum.
Exact PID/start/profile/commit/executable/child-role provenance is emitted by
the fixture. No aggregate or capture route was run.

### Primary worker-start failure precedence and retry (DLV-235)

The native bridge adapter now carries the existing typed asynchronous
`connectionFailed` worker category across the private establishment seam. The
session coordinator classifies that category as `Start` while retaining the
separately sanitized bridge message only for presentation; no diagnostic text,
widget identity, or service name participates in control flow, and the public
bridge protocol is unchanged.

A same-runtime Start failure remains authoritative when an automatic lifecycle
target advances from Visible to Interactive. The coordinator revokes queued or
in-flight work at a new generation boundary and suppresses further automatic
lifecycle establishment while the failure is current. Explicit Retry owns one
fresh restart generation, but the primary failure remains visible until that
generation admits a valid snapshot. Only admission clears the failure; stale
or secondary snapshot/lifecycle results cannot replace it. Existing
coordinator, bridge, lifecycle, failure, input, focus, presentation, and
process owners remain sole authority.

Focused Release evidence passes 13 coordinator scenarios, including a
deterministic blocked-start retarget/revocation/retry case, plus the native
bridge/catalog parser with direct `connectionFailed` versus `processExited`
category coverage. The PATH-isolated `WidgetActionFailureHostTestsOnly` route
retains the actionable YT Music worker-start failure without the secondary
hidden-cache diagnostic, recovers through one A-button Retry and one worker,
restores exact play/pause UIA focus, status/live-region/action evidence, keeps
feedback bounded across hide/reopen, and leaves no worker after normal close.
No aggregate, capture, widget-domain, or unrelated full-build route was run.

### Generic worker crash isolation and inert last-good recovery (DLV-232)

The existing bridge event pump now hands each typed, sanitized runtime-failure
event to the native session coordinator instead of retaining presentation text
alone. A post-admission worker exit keeps that widget's own last-good snapshot
available for visual rendering, while coordinator presentation authority marks
it failure-current. The host immediately clears pressed/slider/focus/rendered
hit-test/UIA state, publishes no widget quick actions, stops declarative motion,
and paints the retained content with `semantics=inert`; it does not substitute a
different widget's committed pixels. Shell Back remains available, but only a
pressed A Retry or the existing generic Hold-Y route can request one fresh
generation. Repeats and all other failed-widget actions are inert. A valid
fresh-generation snapshot admission is the only lifecycle completion that
clears failure and restores interactive authority, and an existing primary
Start failure retains precedence over a later secondary runtime-exit failure.

Focused Release evidence passes the native bridge runtime-failure parser,
13 coordinator scenarios, Overlay state, Widget lifecycle, 305 action-feedback
checks, 68 content-targeting checks, and 112 controller-navigation checks. The
process/job cleanup fixture passes 20 checks plus its exact owner/client process
case, and the completed native Release host compiles without packaging. Static
review traces `WidgetProcessClient.OnProcessExited` through the typed bridge
failure publication, bounded native event pump, coordinator failure state,
inert rendering/input/UIA projection, fresh-generation restart/admission, and
kill-on-job-close teardown. The actual OS/AppContainer crash induction remains
an explicitly untested residual risk because the rejected cross-boundary crash
oracle was not redesigned or rerun. No test-only production escape hatch,
public protocol change, provider/domain behavior, Tier 3, capture, or packaged
integration route was added or run.

### Atomic destination geometry admission (DLV-238)

The native host now keeps a cold destination's retained source snapshot and
extent only as inert visual presentation until the destination snapshot is
admitted. Admission resolves the destination's own surface request and Taffy
layout, renders one complete frame at that destination viewport, then commits
the frame and the bottom-anchored HWND/container placement from one typed
presentation directive. DirectComposition may interpolate the complete
destination inside the old visual envelope, but destination layout never uses
the retained source viewport. The terminal motion commit explicitly restores
identity scale/offset and settles the HWND at the destination placement.
Reduced motion commits that final geometry immediately. Intermediate render,
WM_SIZE, and WM_WINDOWPOS notifications cannot publish or clear destination
UIA before the placement transaction owns its geometry; the exact destination
tree is published after commit, and retained content remains inert beforehand.

The staged native responsibility map is:

| Concern | Before | After |
| --- | --- | --- |
| retained snapshot/focus/surface request | six mutable `OverlayApp` fields and draw-time selection | private `OverlayPresentationTransaction` retained authority, exposed read-only to rendering and surface policy |
| desired versus presented extent | `OverlayApp` timeline plus optional extent fields read independently by placement, input, UIA, and paint | one transaction timeline; destination layout extent is carried separately from the animated presented envelope |
| composition motion and final geometry | eight `OverlayApp` motion/placement/scale/count fields plus ad-hoc terminal clearing | typed admission/motion directives and one explicit destination-settlement state machine |
| composition child ownership | one transformed DirectComposition visual carried content, guide, and tray together | the sole `OverlayCompositionSurface` root retains content, guide, and tray child visuals/surfaces; only content receives motion transforms/clips |
| host-chrome invalidation | every host invalidation repainted the complete content-plus-chrome surface | guide and tray revisions are independent; `RequiresTrayRepaint` replaces the complete bounded tray surface once for every tray-owned state change, while content-only updates retain it |
| presented coordinate authority | pointer and UIA shared one whole-frame inverse transform while diagnostics sampled untransformed local chrome bounds | one child-coordinate plan projects animated content and identity-scaled chrome; pointer, hit testing, focus/UIA, and actual screen-bound diagnostics use those same spaces |
| OS ownership | `OverlayApp` owns HWND, D2D/DComp, focus, input, UIA, and Taffy orchestration | unchanged; the transaction owns no HWND, timer, renderer, compositor, focus graph, or input path |

The worktree clangd index was refreshed for 100 native translation units, and
definition/reference queries resolved every extracted presentation symbol
(`RetainAdmittedWidget`, `PresentedExtent`, `BeginExtentTransition`,
`PrepareCompositionAdmission`, `PrepareCompositionStep`, `CurrentMotionPlan`,
and `RetireHidden`) between `main.cpp` and the new private owner. Focused
Release evidence passes placement 112,333/112,333, targeting 68/68,
transition/transaction 73/73, chrome 45/45, accessibility provider 155/155,
focus 49/49, host accessibility 34/34, declarative renderer 4,851/4,851,
surface coordinator 80/80, and Taffy layout 250/250. The final native Release
host compiles successfully without packaging or aggregate execution.

The bounded production-host route admitted all eight differently sized widgets
through the ordinary worker path and passed the new per-transition requirement
that admitted desired extent and viewport differ from retained source geometry.
On the live 5120x1440, 125%-DPI profile, Network Controls correctly resolved
its FillAvailable destination to 4048x878 and rendered that complete destination
while the initial presented envelope remained the prior Games & Apps 892x608;
the deterministic compact-profile transaction case separately proves the
required Audio Mixer 592x698 to Network 632x878 admission and settlement. The
route also reached its pre-existing slow-worker close/reopen tail, where it
timed out waiting for a fresh Settings admission after the old bridge had been
revoked. That late DLV-231 recovery assertion is retained unchanged and the
packaged route was not repeated; therefore OS-level close/reconnect recovery
after the geometry assertions remains an honest residual evidence gap, not a
passing DLV-238 claim. No Tier 3, capture, provider/package, second HWND,
renderer, focus, input, UIA, or layout authority was added.

#### Cumulative independent-chrome correction (DLV-238 + DLV-236)

The sole DirectComposition target now has one root and retained content,
controller-guide, and tray child visuals/surfaces. Destination content alone
receives the envelope scale/offset/clip. The guide uses the stable tray-width
bottom-center band, and guide/tray visuals remain identity-scaled while their
root-relative offsets follow the final destination inside the union HWND. One
`CommitFrames` call attaches all replacement children, applies the content and
chrome presentations, and commits the existing root once; no HWND, device,
target, root, renderer, provider/tree, focus graph, hit-test owner, or input
router was added.

Tray invalidation is retained independently of content. Appearance, DPI,
catalog/order, selection/reorder, layout, access-policy, or device recreation
rebuilds the complete bounded tray surface exactly once; tile-level
DirectComposition damage is removed. Snapshot admission, provider publication,
slider/scroll/content-focus work, and composition motion retain the tray
background and icons. The production diagnostic reports per-child paint
counters and actual screen rectangles rather than treating untransformed local
bounds as stationarity evidence.

The existing UIA provider now projects widget-domain nodes through the animated
content child and host/tray nodes through identity-scaled chrome coordinates.
The same `PlanCompositionChildCoordinates` mapping drives content pointer
inverse, fixed tray hit testing, authored-surface hit testing, UIA bounds, and
start/mid/end diagnostics at each committed motion step. The one provider root
continues to expose the union HWND; retained pre-admission content remains
non-actionable.

The refreshed clangd index covers 100 translation units. Definition/reference
queries plus `rg` resolve the moved `CommitFrames`, `ApplyChromePresentation`,
`RenderCompositionFrames`, `CurrentCompositionChildCoordinates`,
`RequiresTrayRepaint`, and `PlanCompositionChildCoordinates` ownership across
`OverlayCompositionSurface`, `OverlayApp`, `OverlayChrome`, and
`OverlayTargeting`.

Focused Release evidence passes placement 112,333/112,333, targeting 76/76,
transition 73/73, chrome/invalidation 52/52, accessibility provider 158/158,
focus 49/49, host accessibility 34/34, and declarative renderer 4,851/4,851.
The native Release host build is green. The final bounded production route
passes all eight differently sized widgets and eight variable-extent motions;
actual guide/tray rectangles remain identical through motion, rapid reversal
performs one complete repaint for each tray-owned selection change, and tray
paint counters do not advance during
content-only motion, odd-width union containers retain the exact integer
destination offset through settlement, and job cleanup leaves zero observed
processes. Retained route provenance is base `3716063`, host SHA-256
`824e5a9f3b9efe7939d7dc0cbcae94ec4a2cfb8beef92400a5e0bf1c176e4ae8`,
root PID 20712, with one bridge and eight fixture workers. The explicit
geometry-only stop occurs before the inherited DLV-231 close/reconnect tail;
that unrelated tail was not rerun. No Tier 3, capture, provider/package, or
public-protocol work was performed.

#### Session-owned tray correction after live inspection (DLV-238 + DLV-236)

Live cycling showed that geometry-only evidence was insufficient: the icon tray
could disappear even while its logged rectangle stayed fixed. The native host
now keeps one session-owned tray and guide crop, raster extent, and absolute
bottom-center screen rectangle for the visible overlay session. The union HWND
may move around that fixed chrome rectangle, but only the destination content
child receives the animated transform and clip. Entering or moving within
widget content changes input and UIA authority only; it cannot repaint or clear
the retained DirectComposition tray surface. A widget selection remains a
tray-owned event and replaces the complete small tray surface once, with its
selection indicator safely inside the retained crop.

The focused Release composition suite passes placement 112,333/112,333,
targeting 76/76, transition 73/73, chrome 51/51, accessibility provider
158/158, focus 49/49, host accessibility 34/34, and declarative renderer
4,851/4,851. The final bounded production eight-widget route passes with seven
variable-extent motions and zero observed process leaks. It captures one tray
rectangle for the visible session and compares its exact left, top, right, and
bottom before selection, after selected identity changes, before admission,
and at start/mid/end/final motion settlement; an exact one-pixel 735/736 width
drift fails the fixture. The route separately permits the one complete repaint
needed for a selection update and requires no further tray paint during the
ordinary content-motion interval. The native Release host compiles successfully.
No capture tooling, Tier 3, provider/package change, second HWND, or additional
renderer/input/focus/UIA authority was introduced.

### Correlated widget-selection admission observability (DLV-237)

The production host now assigns one bounded correlation ID when tray selection
changes widget authority and carries it through the existing posted/dequeued
snapshot refresh, lifecycle Establish decision, and sole worker-session queue.
Typed records distinguish queued, deduplicated, replaced, and skipped lifecycle
work with a bounded reason; worker request queue/start/complete records retain
request ID, generation, lifecycle, request kind, queue latency, worker duration,
and admitted/failed/stale-generation/wrong-lifecycle/cancelled disposition. The
only controller action record is a real Visible-to-Interactive activation.

`WidgetAdmissionTrace` is a private observer: it owns no selection, lifecycle,
transport, process, presentation, focus, or input decision. It retains at most
16 transitions and 32 records per transition, sanitizes bounded identifiers,
emits a single slow marker after 250 ms, and drains at most 64 formatted
diagnostics to the existing log sink on a background thread. No payload,
snapshot body, controller-repeat, or paint record is retained.

Focused Release evidence passes `WidgetSessionCoordinatorTests` with 15
scenarios, including correlated deferred/queued/started/admitted stages,
monotonic request timing, refresh queue latency, one-shot slow and meaningful-A
records, sanitization, and bounded eviction. The same route passes
`OverlayStateTests`, `WidgetLifecycleTests`, and `WidgetActionFeedbackTests`
(305 checks). A single native Release compile produced `OverlayHost.exe` with
the observer wired to the ordinary production selection/session seams. No
physical reproduction, behavior correction, Tier 3, capture, provider/package,
or public-protocol work was performed; the intermittent selection defect remains
for evidence collected from a future affected session.

#### Correlation-integrity correction

The selection correlation is now bound to the exact selected or active widget
instead of every desired lifecycle target. Pinned or background lifecycle work
therefore carries no selected-transition correlation. The private trace rejects
an event whose sanitized widget identity does not match its tracked target
before it can record a lifecycle result, terminal admission state, or a
meaningful-interactive transition. The deterministic `WidgetSessionCoordinatorTests`
route now has 17 scenarios: selected-plus-pinned activity proves the pinned
request cannot use correlation 92, and a rapid supersession/cancellation case
proves only the tracked destination terminalizes while the current transition
still emits its one truthful slow marker. The focused Release route also passes
`OverlayStateTests`, `WidgetLifecycleTests`, and `WidgetActionFeedbackTests`
(305 checks). A native Release compile succeeds. This remains observability
only: no lifecycle behavior, protocol, rendering, focus, or input authority
changed.

### Fixed chrome companion correction (DLV-244)

The fixed guide/tray endpoint is now an owned, topmost, non-activating tool
popup of the existing content/session HWND. The existing window procedure keeps
the sole pointer owner: guide/tray pixels return `HTCLIENT`, transparent gaps
pass through, and a tray release is transformed back to the content owner's
established activation path. Ordinary show, hide, close, initialization
fallback, composition failure, and shutdown reconcile the content/chrome pair;
the fallback hides the independent chrome endpoint before legacy content-window
rendering resumes.

One canonical semantic tree is partitioned into content and chrome endpoint
trees. Each element is published once, each `ProviderHost` is bound to its real
HWND, chrome bounds are made local before the chrome root applies its screen
transform, and both action queues return to the existing logical action/focus
owner. Content UIA now follows only the animated content transform; fixed tray
focus and actions remain on the identity-scaled chrome root.

The retained chrome-session key now includes the applied work area, DPI,
interface/accessibility scale, appearance revision, and exact catalog order.
Content resize or widget identity alone cannot move it, while same-DPI monitor
work-area changes and installed/order changes rebuild the crop and placement.
Direct real-HWND policy evidence covers owned-popup styles and lifecycle, eight
distinct content extents forward and reverse, hide/reopen, exact odd/even
fractional-DPI bounds, hit/pass-through regions, and work-area/catalog/scale/
appearance invalidation. Direct real UIA client evidence proves distinct
content and chrome roots, exact HWND screen rectangles, single publication of
tray semantics, and chrome-only logical tray focus.

The final focused Release composition group passes placement 112,333/112,333,
targeting 76/76, transition 73/73, chrome/window policy 101/101,
accessibility provider 165/165, focus 49/49, host accessibility 34/34, and
declarative renderer 4,851/4,851. A native Release build produces
`OverlayHost.exe` without packaging. No Tier 3, capture, provider/widget,
public protocol, second session/input/focus/accessibility authority, or push was
performed. Physical user cycling of the exact accepted candidate remains the
final pixel-level stationarity verdict.

#### Atomic paired-endpoint recovery correction

Content-target and chrome-target initialization now pass through one private
fixed-chrome composition policy. If the content target succeeds and the chrome
target fails, that policy resets the complete DirectComposition surface owner
and hides the chrome HWND before legacy fallback can become current. Runtime
composition/device-loss fallback and shutdown use the same paired reset, so
`available()` cannot advertise a half-session and no independent chrome window
remains visible. The existing graphics device, session/window owner, renderer,
input/focus router, and accessibility policy remain unchanged.

The chrome window's production pointer mapping is also a named private policy:
it maps one real `WM_LBUTTONUP` from chrome-client through screen coordinates
to content-client coordinates, then calls the existing activation owner. The
direct HWND test applies the owned chrome rectangle, sends that window message
at the second authored tray tile, and proves the existing `OverlayState`
selection/activation authority selects and activates `network` exactly once.

Deterministic recovery evidence forces the second-target failure while the
content target is observably available and then proves composition unavailable
plus chrome hidden. A real successful two-target DirectComposition session is
then forced through the runtime reset and proves the same terminal state. The
focused Release composition group passes placement 112,333/112,333, targeting
76/76, transition 73/73, chrome/window/recovery 111/111, accessibility provider
165/165, focus 49/49, host accessibility 34/34, and declarative renderer
4,851/4,851. One native Release build produces `OverlayHost.exe` without tests
or packaging. No launch, Tier 3, capture, provider/widget, or public-protocol
work was performed; physical user cycling remains the final visual verdict.

#### Physically accepted destination authority and focused regression lock

The user physically accepted fixed-tray behavior and the cumulative destination-
size correction through `7420d7a`. The existing presentation transaction now
retains the last successfully committed widget identity and destination extent.
An asynchronously admitted widget therefore compares its newly resolved desired
extent with committed geometry even when the session model changed before the
refresh wrapper ran. The accepted Network-to-YT case advances from committed and
presented `560x645` to authored `760x385`; once that destination commit succeeds,
later same-destination lifecycle or snapshot refreshes remain repaint-only and
cannot restart placement or motion.

Post-acceptance focused tests expose, without changing the accepted behavior,
the BeginDraw physical-pixel-to-DIP normalization at 125% and 150% DPI, content-
only frame-offset ownership, fixed guide/tray offset latching across repaint and
surface replacement, panel-local geometry without external chrome reservation,
the authored panel-to-guide gap at motion start/midpoint/settlement, and committed-
destination authority across late admission and same-destination refresh. Old
expectations that inferred fixed chrome from a combined shell rectangle were
removed or replaced with companion-HWND/local-surface expectations.

The focused Release composition group passes placement 112,340/112,340,
targeting 76/76, transition 83/83, chrome/composition/window 119/119,
accessibility provider 165/165, focus 49/49, host accessibility 34/34, and
declarative renderer 4,851/4,851. The updated bounded widget-switch geometry
fixture compiled, but its isolated temporary host failed before authenticated
readiness with empty overlay and startup-error logs, so none of that route's
eight-widget assertions executed. A reviewer-owned accepted host process held
the ordinary Release link output; it was not terminated. The route was not
repeated or redesigned after the proportionality stop. Real Z-order source
review confirms backdrop/content placement uses `SWP_NOZORDER`, followed by one
deferred `chrome > content > backdrop` operation, while the fixed chrome policy
retains its owned topmost popup role. Process ownership remains one per-user,
profile-scoped owner with the existing authenticated activation pipe.

### Pending admission-stall evidence (DLV-239)

This observation is recorded only for the next DLV-239 assignment and was not
addressed by DLV-244. Live transition 23 at 06:54:33 selected `game-launcher`
with `currentSnapshot=false`; refresh dequeued after 78 ms, lifecycle Establish
was skipped with `reason=already-current`, and no snapshot request queued,
started, or completed. The trace crossed its 250-ms slow threshold while retained
Games & Apps pixels stayed visible. The user closed the overlay via Guide at
06:54:35.707 and reopened it at 06:54:36.338. Only that new visible session then
committed and triggered the Game Launcher extent refresh/sequence 10 from
06:54:36.464 onward. There was no automatic recovery while the overlay remained
open; close/reopen was required. Transition 22 to Games & Apps had queued,
started, and completed Establish normally. The evidence shows snapshot eviction
and lifecycle-current state diverge; DLV-239 must retain the last admitted
checkpoint and/or ensure `RefreshRequested` queues a snapshot when lifecycle is
already current.

### DLV-239 — retained checkpoint and explicit refresh state

The native `WidgetSessionCoordinator` remains the sole semantic checkpoint,
lifecycle, request, cancellation, and completion owner. Each catalog widget now
has explicit `Current`, `RefreshRequested`, or `RefreshInFlight` freshness state
beside the existing single last-admitted checkpoint. Ordinary provider/hidden
and appearance-derived invalidation records refresh demand without deleting the
checkpoint or waking a background worker. Selection can therefore use that
widget's own retained snapshot and authored surface hints immediately while the
same request owner establishes or refreshes current state. An already-current
lifecycle no longer suppresses requested snapshot work.

Retained refresh or failure content is visual-only. Native interaction, focus,
quick-action, and UIA authority continue to resolve only a `Current`
presentation. A successful current request atomically advances checkpoint and
action authority; failure, cancellation, stale completion, and a newer demand
arriving during an in-flight request leave the last-good checkpoint inert. The
existing bounded queue either retains the newer demand or runs one queued
follow-up. Hard checkpoint removal is now concentrated at explicit restart,
widget removal/runtime or presentation-generation replacement, protocol/
instance mismatch, and the existing explicit hard-removal API. Derived renderer
resources and appearance refresh remain separate from semantic retention.

Deterministic Release evidence passes `WidgetSessionCoordinatorTests` with 19
scenarios plus `OverlayStateTests`, `WidgetLifecycleTests`, and 305
`WidgetActionFeedbackTests` checks. The all-eight scenario retains eight exact
surface extents, observes zero background snapshot calls across ordinary/
appearance refresh demand, admits a retained lookup within the existing 20-ms
test bound, and covers resident already-current refresh, unloaded selection,
failure, cancellation/rapid switch, restart, protocol mismatch, cache count,
and inert-versus-current authority. The focused composition group passes
placement 112,340/112,340, targeting 75/75, transition 83/83, chrome 119/119,
accessibility provider 165/165, focus 49/49, host accessibility 34/34, and
declarative renderer 4,851/4,851.

One allowed linked widget-switch attempt stopped before running the host route:
the unchanged `WidgetSwitchFixture` publish exited 1 before an executable
fixture or authenticated host session was established. It was not retried or
redesigned, and none of that route's assertions count as evidence. Direct source
review confirms the coordinator still bounds pending work to 32 requests,
revokes generation-mismatched work through its existing cancellation owner,
drains lifecycle through the same shutdown path, and retains no second cache or
lifecycle owner. A native Release build succeeds without tests or packaging;
`OverlayHost.exe` SHA-256 is
`88146E53891572DCB21E5CC068870BF70233FE75B8A2D92C3AC6C7B23A6787E5`.
No public protocol/SDK, residency policy, incremental Taffy/damage, aggregate,
launch, capture, provider, packaging, or push work was performed. The linked
host continuity route and physical retained-selection latency remain residual
planner/user verification.
### Autonomous full-trust Community Game Launcher (DLV-217)

Game Launcher now ships as `org.gbar.community.reference.game-launcher` through
the generic `full-trust-application-v1` runtime. The package executable owns its
Windows/Xbox and opt-in installed-store source composition, source health, bounded
cursor queries, opaque SavedId key, organization-state CAS file, running-app
observation, and exact launch revalidation. It uses the public normalized SDK
presentation values and generic application bootstrap; the staged payload contains
neither `PlatformBroker.dll`, `WindowsAppLibraryProvider.dll`, nor
`PlatformSettings.dll`, and the manifest declares no product capability.

The external export produces the same autonomous executable from only the local
public SDK package and copied package source. The ordinary catalog route requires
explicit full-trust consent, starts the exact immutable executable without worker
arguments or AppContainer identity, returns a valid credential-free snapshot, and
supports disable/uninstall cleanup. Package-local organization state is a narrow
pre-release reset from the retired overlay state; the existing Epic/GOG opt-in
booleans are imported once, while external store/account data and credentials are
left untouched. Game Launcher appears as Community; Games & Apps remains Built-in.
Focused Release evidence passes Game Launcher 90/90, package-owned persistence
3/3, Windows app-library source behavior 75/75, Gbar CLI/export/package 65/65,
and Widget Bridge 88/88 including the ordinary full-trust application route. The
supported package helper produced a 13-file, 1,952,494-byte immutable 0.2.0
archive. Documentation validation reaches only the seven pre-existing broken
links in reviewer-owned delivery-plan history; no implementation-owned document
failure was reported.
The one clean exact-commit Tier-3 checkpoint at `7aa229e` is retained under
`artifacts/verification/20260813T131146Z-a314fbcd`; it stopped in the runner
self-test before product execution because the new MSTest project was absent
from the verification manifest. The appended verifier registration is proven by
the bounded 3/3 step at
`artifacts/verification/20260813T131228Z-bb1ba3ef`; the canonical aggregate was
not repeated.
