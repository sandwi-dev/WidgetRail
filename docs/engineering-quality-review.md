# Engineering Quality Review

Status: living independent quality audit; active findings require disposition<br>
Date: 2026-08-09<br>
Last reassessed: 2026-08-09 after committed digest-bound GBSS and typed source diagnostics, aggregate catalog scaling, native host widget-session ownership, advanced-widget SDK adoption, and retained visual/performance evidence were audited<br>
Scope: architecture, maintainability, correctness, security, performance,
verification credibility, UI/UX foundations, and product readiness

## Executive assessment

The quality trajectory is **improving, but the repository is not yet at the
standard of a cohesive senior platform team**.

There is substantial good engineering here: the installed-widget runtime uses
an explicit AppContainer and broker boundary; protocol and package inputs are
heavily bounded; managed builds treat warnings as errors; deterministic tests
cover many lifecycle and controller contracts; performance claims are
separated from targets; and the recent SDK work is replacing repeated task,
cancellation, paging, state, navigation, and command plumbing with explicit
public abstractions.

The strongest concrete improvement since the previous audit is the verified
package-evidence contract. Committed work bounds manifest/metadata consumption,
rejects short/extra/changed tree input, and parses the exact manifest bytes
included in the digest. Current HEAD also emits an exact GBSS path/
SHA-256 inventory and requires every host-compiled entry/import to match it after
one bounded strict-UTF-8 read. The implementation agent reports catalog 28/28
and Styling 23/23; this review code-inspected but did not execute them. This
closes the resource, manifest-pairing, and style-consumption subproblems while
leaving executable/dependency/asset launch authority open.

The public authoring entry point is not yet a coherent shipped product. The
current HEAD removes its misleading external success path: `gbar new widget`
no longer emits an unpublished placeholder SDK reference. It resolves a real
source project before creating output, accepts an explicit validated
`--sdk-project`, and otherwise fails with no partial scaffold. A new temporary-
directory case copies the template outside the checkout, proves rejection
without an SDK, generates with the override, and builds the result. This is an
honest contributor workflow, not yet a standalone community product: the
generated repository still points back to the platform source tree, no SDK
artifact is published, and the README refers snapshot export to typed-fake
tests the template does not generate.

Current HEAD closes the remaining CLI author-code bypass: `gbar render`
now accepts only bounded snapshot JSON, and DLL input fails closed before type
resolution or output handling. `gbar dev` remains the executable integration
path through the production AppContainer worker boundary. Current HEAD
also resolves the prior responsive-focus
identity risk by separating focus persistence from action and source-element
routing. It also moves reconciliation out of steady paint and onto relevant
state transitions. The implementation agent reports that focused managed and
native Release tests, the OverlayHost Release build, and the repository-wide
Release verifier all pass on current HEAD; this review did not rerun
them or inspect a retained machine-readable result.

A separate public-distribution blocker is the incomplete publisher/provenance
model. Current HEAD now makes the immediate Settings decision honest:
it labels Community packages unsigned, identifies the manifest publisher as
unverified, shows the sealed digest, and binds enablement/consent language to
those exact content bytes. It still cannot show a host-owned acquisition
receipt, verified signer, rotation, or revocation because those models do not
exist yet. The deeper launch audit also found that the digest-derived identity
is published before the generic worker later reopens mutable assembly and
dependency paths. Current HEAD binds host-compiled GBSS and imports
to the verified relative-path/SHA-256 inventory, but no launch lease currently
binds worker-loaded executable bytes or general assets to that identity. This
is now the highest-risk package-boundary finding.

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
not yet provide an automated, bounded, reproducible GitHub gate. The extensive
bug ledger has accumulated 55 simultaneously active `Verifying` entries,
including 22 P0s, which makes priority and release status difficult to trust.

The native-host rotation makes that ownership concern concrete. `OverlayApp`
occupies about 3,753 lines and directly composes bridge transport, catalog
generations, snapshot authority, lifecycle, retry, input/focus, presentation,
rendering, and user-visible failure state. The right first extraction is a
tested `WidgetSessionCoordinator` above `WidgetBridgeClient`, not another pure
helper or a broad UI rewrite. Besides reducing change coupling, this is the
natural owner for the typed persistent capacity/startup failures still missing
from the product UX.

## Changes since the previous audit

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
| Widget Catalog | 28/28 passed |
| Widget Styling | 23/23 passed on current HEAD |
| Platform Settings | 15/15 implementation-reported on the typed-diagnostic milestone |
| Focus Navigation | 41 checks passed |
| Declarative Renderer | 4,632 checks passed |
| Widget bridge catalog | passed |
| OverlayHost Release target | built successfully |
| Documentation contract | 49 Markdown files passed on the typed-diagnostic milestone |
| Full `scripts/Verify.ps1 -Configuration Release` | passed in 283.5 seconds before GBSS-only narrowing and typed diagnostics; affected focused suites are green |

The typed-diagnostic milestone is committed. Styling 23/23, CLI 49/49, and the
49-file documentation contract were run and their output inspected around the
final API-shape change; Platform Settings 15/15 and Bridge 40/40 were reported
green earlier in the same milestone. The previous full-verifier run predates
this commit, so it is not retained end-to-end evidence for current HEAD.

The implementation agent reports that the full verifier additionally exercised
the managed, protocol, capability,
documentation, packaging-conformance, native input/layout/rendering, hidden
OverlayHost smoke, and InputProbe paths. The repository still retains no
machine-readable per-run result or immutable CI link, and this review did not
independently execute the commands, so EQ-004 remains open.
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
The unsigned-review Settings changes are committed, but their focused 41/41
result remains implementation-reported and the prior full-verifier claim
predates them.
The residency budget, runtime lease, scaffold residency default, and focused
tests are committed in `7722763`. The implementation agent reports 36/36
runtime and 40/40 bridge cases; the new direct lease cases were code-inspected,
but no retained result was inspected.
The committed scaffold milestone adds an outside-repository negative/override
generation case and invokes `dotnet build` on the generated project. This
review inspected but did not execute it. The case still supplies the template through
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
version limits, or retained full-verifier output tied to current HEAD.

## Prioritized findings

### EQ-001 — P0 — CLI inspection executed author code outside the production sandbox

**Status: Implemented in current HEAD; focused Release verification is reported, retained evidence is pending.**

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

**Resolution evidence.** The implementation agent reports that Release
`GbarCli.Tests` passes 48/48. Source inspection confirms that the render
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

**Evidence.** `InstallCommand` requires `--sha256` for HTTPS/GitHub packages,
the downloader applies bounded HTTPS/redirect/size/time rules, and installation
leaves remote packages disabled. `WidgetCatalogService` rejects updates while
an ID is enabled, keeps versions immutable, and requires explicit version
selection. `InstalledPackageIntegrity` seals the extracted content tree, while
`InstalledWidgetAuthority.PublisherId` derives an `unsigned.<digest>` runtime
authority so a replacement observed during catalog validation receives a new
identity instead of inheriting consent or secrets. These are strong integrity
and containment foundations, but EQ-014 shows that a mutable path is reopened
after verification, so the exact-byte property is not yet atomic through
worker load.

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
The implementation agent reports 36/36 runtime and 40/40 bridge cases; this
review did not execute them or inspect a retained result.

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

The retained schema-2 performance baseline does not yet prove the aggregate
contract. It launches only the trusted Settings worker, takes 31 observations
per state in one dirty-tree run, and reports Hidden CPU p95 of 0.0977% against a
0.1% diagnostic target—too little margin and repetition to establish a noise
envelope. Hidden also records 31.65 Guide-compatibility timer messages per
second. The harness correctly labels these as host messages rather than OS
wakeups and reports GPU, scheduler, presentation, controller latency, and
long-run trends as unavailable. Visible/Interactive aggregate working set
reaches 203.6/214.3 MiB, but Settings' trusted control-plane exception makes
that run unsuitable for extrapolating ordinary Community-widget cost.

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

**Resolution evidence.** The implementation agent reports Release
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

### EQ-015 — P1 — The generated widget lacks a published standalone SDK/test contract

**Status: Partially implemented in current HEAD. Local-project SDK
resolution is truthful and build-tested; published SDK and generated snapshot/
test contracts remain open.**

**Evidence.** `NewCommand` now resolves the source checkout or requires an
explicit existing non-reparse `WidgetSdk.csproj` through `--sdk-project`. It
does so before creating the output directory, escapes the relative MSBuild
path, and fails with an actionable message rather than emitting the unpublished
`GameBarAlternative.WidgetSdk` placeholder package. The help, CLI README,
quickstart, authoring, publishing, and troubleshooting guides state the same
temporary local-project contract.

Current HEAD corrects two immediate template defects. The generated
README now uses `gbar dev` for executable integration and accurately says
`gbar render` consumes snapshot JSON only. The manifest and Clock sample switch
from permanent `keep-alive` to `unload-after-idle` at 300 seconds, and the CLI
test asserts that residency metadata. A new test copies the template under an
unrelated temporary root, proves unresolved SDK discovery leaves no output
directory, supplies the exact SDK project, and successfully runs a bounded
Release build of the generated widget. However, the README tells authors to add
a typed-fake snapshot exporter while the template contains no test project,
exporter, or static snapshot, so its subsequent `gbar replay` path still has no
generated input.

**Why it matters.** The misleading successful-but-unbuildable scaffold is now
removed. Standalone GitHub repositories—the intended sharing unit—still cannot
consume the SDK through a supported published dependency, and developers must
bring a platform checkout plus invent a snapshot-export path. That remains
short of the promised 15-minute community starter experience.

**Underlying problem.** The generator now treats local SDK resolution as a
validated dependency, but the CLI, template, SDK/runtime package, generated
tests, and copyable commands are not yet shipped as one versioned release set.
Generated documentation also remains outside executable documentation checks.

**Recommended direction.** Treat the CLI, template, SDK/runtime packages, and
compatibility range as one release set. The production endpoint is a supported,
immutable NuGet SDK/runtime release plus a template that pins a compatible
version and can be restored from a clean machine without the platform source.
The current explicit local-project override is appropriate for contributors
until that artifact exists; do not reintroduce a project known not to build.

Keep the corrected `gbar dev` and idle-unload defaults. Add a real generated
typed-fake test/exporter or a validated static snapshot so the documented
data-only render/replay path is executable until the isolated scenario worker
exists. The public SDK already exposes `WidgetTestHost`,
`WidgetTestHostServicesBuilder`, and `SnapshotJson`; the starter can generate a
small deterministic test executable that attaches the widget, asserts initial
and post-action state, and writes the exact `snapshot.json` consumed by the
existing replay. That is preferable to instructing a first-time author to
invent infrastructure the platform already owns. Keep advanced focus/shortcut
examples, but make the first README path the smallest complete build-run-test-
package loop.

**Tradeoff.** Failing outside the checkout temporarily exposes an unfinished
product instead of appearing convenient, but it prevents hours of misleading
restore troubleshooting. Publishing packages creates versioning, symbol/source,
provenance, and support obligations; vendoring SDK binaries into each scaffold
avoids a feed but produces opaque duplication and unsafe upgrade mechanics.
An explicit local project override is useful for platform contributors, but it
must not become the documented community distribution model.

**Resolution evidence.** The implementation agent reports Release
`GbarCli.Tests` at 49/49, including a failure-before-write case and an unrelated-
directory explicit-SDK Release build. This review inspected the new case but
did not execute it or inspect retained output. For full closure, run a release
test from a temporary directory with no
repository ancestor and no `GBAR_TEMPLATE_ROOT`: invoke the packaged CLI,
scaffold, restore/build with only declared prerequisites, validate, run the
generated deterministic tests/replay, package, and inspect the result. Execute
or mechanically verify every command in the generated README, including the
data-only snapshot handoff. Add a negative test proving an unavailable SDK
fails during scaffolding with no partial directory rather than later in
`dotnet restore`. Verify the emitted package/template versions match the host
compatibility contract, and retain an external sample repository or immutable
CI artifact as the public proof.

### EQ-002 — P1 — Responsive focus identity required an explicit contract

**Status: Implemented in current HEAD; local verification is reported, retained independent evidence is pending.**

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
implementation agent reports 84/84 managed Widget SDK cases and 41 native
focus checks. It also reports that `WidgetBridgeCatalogTests` passes the native
v13 parser round trip, `DeclarativeRendererTests` passes 4,632 checks, the
OverlayHost Release target builds, and the repository-wide verifier passes.
Source inspection supports the intended contracts; retained independent run
evidence is still absent.

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

### EQ-004 — P1 — There is no automated, bounded repository quality gate

**Status: Open; verification claims expanded without adding a bounded retained-results gate.**

**Evidence.** The repository has strong `Directory.Build.props` defaults and a
large `scripts/Verify.ps1`, but no checked-in `.github/workflows` pipeline.
`Verify.ps1` invokes many `dotnet run`, native build, and process smoke commands
directly; `Invoke-Checked` validates exit codes but supplies no per-step or
overall timeout. The console-style test executables also have no standard test
runner timeout or structured result artifact. Documentation frequently cites a
green full Release gate, but current HEAD has no immutable
CI run associated with it.

`docs/implementation-status.md` now upgrades several focused counts and states
that the current milestone passed the full Release verifier end to end. The
registered managed case totals are consistent with those counts, but there is
still no checked-in workflow, per-step timeout, structured result bundle, or
immutable run reference. The available default Community package artifacts are
older than the source manifests, so they cannot substantiate current packaged
behavior. This is not evidence that the local runs failed; it is evidence that
their result cannot be independently audited or reproduced from the repository.

**Why it matters.** A senior team needs reproducible evidence that does not
depend on one long local agent session. An indefinitely hung test prevents a
credible gate, and unstructured console output makes regression history and
individual flaky tests difficult to track. GitHub distribution without GitHub
validation also leaves contributors unable to prove that a change meets the
same standard.

**Underlying problem.** Verification breadth grew faster than verification
orchestration and evidence publication.

**Recommended direction.** Add a Windows GitHub Actions workflow with a
bounded managed lane and native lane. Give every command and the complete job a
timeout; upload machine-readable test/build logs and key package hashes. Keep
hardware, live-auth, real-controller, and physical-display checks as explicit
manual release gates rather than pretending hosted CI can cover them. Refactor
`Verify.ps1` so the same bounded command manifest drives local and CI runs.

**Tradeoff.** Migrating every custom executable test to a third-party framework
is not required immediately. A thin runner can add subprocess timeouts and
JUnit/TRX-compatible records first. Native and AppContainer tests may require
separate permissions or self-hosted evidence; isolate those rather than
dropping the entire gate.

**Resolution evidence.** A clean commit must produce a repeatable Windows CI
run with bounded durations, managed and native results, documentation-link
validation, deterministic package checks, and retained logs. A deliberately
hung fixture must be terminated and reported as a failed test without hanging
the job.

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

### EQ-006 — P2 — Advanced widgets remain application-sized monoliths

**Status: Partially improving; Media Sessions proves the SDK direction, but
the advanced-widget migrations have not yet established a repeatable
application structure.**

**Evidence.** Current primary files are approximately 1,176 lines for
`samples/YtMusicWidget/YtMusicWidget.cs`, 2,023 for
`samples/SpotifyWidget/SpotifyWidget.cs`, 2,020 for
`src/FirstPartyWidgets/NetworkControlsWidget/NetworkControlsWidget.cs`, and
2,496 for `AudioMixerWidget.cs`. Line count is only a locator for the deeper
ownership issue.

The migrations show three materially different outcomes:

- Media Sessions is the positive control. Its roughly 768-line primary file
  keeps render-facing data in `WidgetModel<State>` and transport admission in
  `WidgetOptimisticCommand`. A textual coordination inventory finds no
  `lock` or `SemaphoreSlim` use and only the lifecycle progress loop/task.
- YT Music uses `WidgetOperations.RunLatest` for one transport-refresh burst,
  including current-attempt guards for success and failure. The same class
  still owns three activation tasks, two semaphores, a state lock, connection
  and polling policy, progress projection, an optimistic-command list and its
  confirmation/rollback algorithm, action routing, and the complete view.
- Spotify has successfully moved two offset collections into
  `WidgetPagedResource<TItem>` and one page family into an Active Latest lane.
  It still has separate command and authorization task registries, action and
  refresh semaphores, an active generation, polling/progress loops, several
  lock domains, state/cache fields, action routing, and all view composition in
  the same class. Audio Mixer and Network Controls have not adopted the new
  model/resource/operation primitives: their primary files contain roughly 61
  and 44 textual lock sites respectively, while retaining handwritten
  pending/authoritative state and lifecycle coordination. Those counts are not
  quality scores; they identify where shared mutable ownership remains
  concentrated.

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
policies are legitimately domain-specific—especially Audio Mixer's
absolute-value command coalescing and confirmation—but their ownership is not
separated from rendering.

**Recommended direction.** Treat Media Sessions as the behavioral baseline,
then create one advanced reference by migrating YT Music before Spotify:
immutable render state in `WidgetModel`, a provider/controller adapter, an
explicit lifecycle coordinator for its polling/progress loops, a command
coordinator that owns pending/confirmation/rollback policy, and pure view
composition. Preserve the current latest-wins transport proof while moving its
state commit seam out of the widget class.

Use that result to migrate Spotify by responsibility rather than by helper:
state/controller/routes/view files, operation lanes for command,
authorization, refresh, and destination loading where their lifetime policies
fit, `WidgetNavigator`/`NavigationShell` for the shared destination model, and
resources for bounded queue/device reads. Do not force Audio Mixer or Network
Controls through a generic abstraction prematurely. First name and test their
domain policies—absolute-value/coalesced audio commands with authoritative
confirmation, and multi-provider event/source merge—then extract narrow
coordinators that can be reused only if a second consumer proves the shape.

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

### EQ-007 — P2 — Copyable documentation examples are not API-checked

**Status: Partially improved; known prose/API drift is corrected, but executable proof remains open.**

**Evidence.** The quickstart now uses the valid `WidgetGlyph.Refresh`, SDK
Gallery distinguishes protocol-v13 focus persistence from action routing, and
the authoring guide now consistently describes additive snapshot versions 1
through 13. `tests/Documentation.Tests/Program.cs` still checks relative links
and selected headings/phrases without compiling fenced C# examples or
validating referenced public symbols. The NavigationShell and starter API
examples are presented as copyable entry points, so link-only validation
remains insufficient.

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

### EQ-014 — P1 — Installed-package verification does not bind bounded verified bytes through launch

**Status: Resource bounds, manifest/digest pairing, and digest-inventory-bound
GBSS/import compilation are implemented with focused coverage; runtime
executable/asset authority still outlives the exact handles that produced it.**

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

The higher-risk gap occurs after verification. Discovery still returns the
package path with the paired manifest and digest. `InstalledWidgetAuthority`
derives `unsigned.<digest>`, and
`BridgeCatalog` uses that identity for the worker fingerprint, isolation key,
and broker authority while passing `--package-root` and `--widget-assembly`
paths to the generic worker. The installer moves the staged directory into the
catalog but does not establish a host-owned immutable generation or retain
open content handles. `WindowsAppContainer.GrantReadAndExecute` restricts the
worker to read access; it does not remove the desktop user's inherited ability
to modify the installed directory. `WidgetAssemblyLoader` later calls
`LoadFromAssemblyPath` for the entrypoint and lazily resolved managed/native
dependencies. Thus package bytes can change after the digest check and before
or during load while authority still names the old digest.

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

A handle lease also needs an explicit namespace invariant. Holding the files
present during hashing prevents those files from being replaced, but does not
by itself prove that a new filename cannot appear later. The worker's
`PackageLoadContext` accepts any contained, non-reparse path returned by
`AssemblyDependencyResolver`; it has no verified relative-path inventory. A
package can therefore carry metadata for a dependency or native library that
is absent during hashing and have that file inserted before lazy resolution.
The same late-addition class still applies to an asset opened directly from the
package root. Exact-tree authority requires
both byte identity for existing entries and namespace membership: reject every
later-resolved path not present in the verified inventory, and prove that the
runtime-visible generation cannot acquire unverified new entries.

Public documentation is mostly aligned with this boundary. The publishing,
security, and GBSS guides now describe the paired manifest/digest result,
digest-inventory-bound style reads, later worker load as non-atomic, and
installed directories as version-addressed rather than OS-enforced immutable.
The terminology sweep is still incomplete: `platform-architecture.md`,
`widget-authoring-guide.md`, `widget-packaging.md`, `implementation-status.md`,
and several publishing/Settings passages still call current-user-owned package
roots or installed versions immutable without consistently limiting that term
to installer no-overwrite behavior. Until a lease/protected generation exists,
the consistent claim is version-addressed, installer-never-overwritten, and
tamper-detected—not digest-bound at every consumer.

The product roadmap's Phase 4 and risk register now name verified namespace/
launch consumption as a separate mandatory pre-public gate alongside signing
and revocation. This preserves the distinction: signing authenticates a
package digest but does not prove that the worker later consumes only that
signed tree.

**Why it matters.** The platform explicitly promises that replacement bytes
cannot inherit consent, configuration secrets, or update authority. A mutable
path checked at catalog publication and reopened later cannot prove that
promise. This is not currently a direct self-tamper vector for the
capability-free AppContainer, which receives read-only access, so it is not a
P0 claim. It is nevertheless a P1 trust-boundary defect before public package
distribution: externally modified or accidentally changed bytes can be loaded
under stale digest-derived authority. The committed bounded-reader work removes the
known oversized metadata pressure path with focused Release evidence,
but it does not close the authority defect.

**Underlying problem.** Verification now pairs manifest, digest, and a GBSS
inventory; the style provider revalidates its inputs against that inventory. Worker startup
still receives a detached path rather than an owned `VerifiedPackageLaunch`
capability whose lifetime covers every executable, dependency, and asset byte.

**Recommended direction.** Retain the narrow bounded-reader design and its
overflow-safe `long` sentinel arithmetic and focused resource-bound evidence.

Current HEAD implements the short-lived publication half by hashing one
inventory, parsing its captured manifest, and compiling only inventory-matched
GBSS/imports. At lazy start, a supervisor-owned
`VerifiedPackageLaunchLease` should reacquire the complete inventory, require
the exact published digest and relative-path set, and retain write/delete-
denying handles for the process session. Managed/native resolution must reject
paths absent from that inventory. A host-owned protected generation whose
namespace and bytes are frozen is an alternative and may simplify direct asset
reads. Merely holding the initially enumerated file handles, hashing again just
before `Process.Start`, tightening a best-effort DACL, or relying on
`FileSystemWatcher` leaves either an insertion or check-to-load gap. The
catalog/supervisor should own both capabilities; the SDK and widget process
should never provide them.
Treat the current raw `VerifiedGbssDigests` dictionary plus production
`InternalsVisibleTo("WidgetBridge")` as an interim host-internal handoff, not as
the final authorization type. Do not spread that friend access into the runtime
or worker. Replace it with an opaque immutable publication/launch capability
whose API can verify text, test inventory membership, and acquire/release the
session lease without letting callers mistake a stale hash map for ownership.
Keep publishing and security documentation explicit about version-addressed,
tamper-detected storage versus atomically verified runtime content during the
transition.

**Tradeoff.** The two-stage model hashes an enabled package during descriptor
publication and again on lazy launch, but avoids retaining up to 512 handles
for every dormant discovered version. A resident launch lease can still block
legitimate uninstall/update until teardown; a protected generation costs disk
I/O and storage instead. Locking only existing files or the entry assembly is
cheaper but does not seal new namespace entries, lazy managed/native
dependencies, or package assets. Choose and measure an explicit bounded model
rather than preserving a cheap but incomplete trust claim.

**Resolution evidence.** Focused catalog cases cover changing length, non-
seekable limit-plus-one input, the manifest/digest result, and exact per-file
GBSS hashes. Styling cases cover consumed-byte overflow, misleading/changing
length, digest mismatch, invalid UTF-8, positive verified imports, and a late-
added import. Add a launch seam
that pauses after catalog verification: replacement before `Process.Start`
must prevent admission under the old digest, and mutation/reversion within the
watcher debounce must not execute. Exercise dependencies and native libraries
that are missing during verification but inserted before lazy resolution, a
late-added ordinary asset, exact authority/inventory derivation
from the launch lease, and lease release on failed connection, crash, restart,
disable/remove, and shutdown.
Retain stable-tamper/live-worker-retirement coverage, and report the handle,
startup, and disk cost at the maximum supported entry count.

### EQ-016 — P2 — Installed catalog work is bounded per version but unbounded in aggregate

**Status: Open. The implementation was narrowed to retain GBSS hashes only,
reducing its incremental cost; the pre-existing aggregate discovery gap remains.**

**Evidence.** `WidgetCatalogOptions` bounds one archive/version to 512 entries
and 64 MiB by default. `DiscoverInstalledVersions` nevertheless enumerates
every ID directory and every version directory with no maximum package IDs,
versions per ID, total versions, catalog files, bytes hashed, or elapsed work.
It verifies and materializes the complete list before catalog state groups or
selects active versions. The committed integrity change additionally builds and
freezes a GBSS-relative-path/SHA-256 dictionary for every discovered version
and retains it on each `InstalledWidgetVersion`, even though only an enabled
active version needs it. It deliberately does not retain hashes for assemblies,
assets, or other package files. `BridgeCatalog` applies its
256-widget limit only after `CatalogService.DiscoverAsync` has completed, so the
limit does not bound enumeration, hashing, allocation, or malformed-version
handling. Settings also consumes the full discovery result.

At default limits, one version can still contribute 512 retained GBSS path/hash
entries and require hashing 64 MiB. Repeated legitimate installs keep old versions for
rollback, and there is no catalog-wide quota preventing those individually
valid versions from multiplying startup/reload work. This review found no N+1
aggregate-bound test or cold-discovery measurement.

**Why it matters.** Catalog reload happens on the product's control path while
the overlay is in use. A large but individually valid catalog can cause long
bridge refreshes, allocation spikes, and delayed Settings recovery while the
user is gaming. Public sharing makes version accumulation normal rather than
an adversarial edge. Per-package safety claims therefore do not establish the
product's lightweight aggregate behavior.

**Underlying problem.** Resource budgets belong to the complete catalog
operation, not only each artifact. The catalog currently combines discovery,
security verification, active-version selection, UI listing, and publication
evidence in one eager materialization. The new inventory is valuable security
evidence but is retained at the broadest scope instead of the active consumer
scope.

**Recommended direction.** Define explicit product limits for installed IDs,
versions per ID, total versions, total enumerated entries, and bytes hashed per
discovery, and enforce compatible quotas in `gbar install` before the atomic
move. Keep failure deterministic and give Settings/CLI an actionable cleanup
path rather than allowing an over-limit tree to make all Community widgets
silently disappear.

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

**Resolution evidence.** Add N+1 cases for IDs, versions per ID, total versions,
aggregate files, and aggregate bytes, proving rejection occurs before hashing
or retaining the over-limit tail. Prove Settings remains reachable and names
the versions that must be removed, and that the bridge does not verify disabled
inactive inventories merely to enforce its 256-widget cap. Record cold and
reload time plus peak memory at the supported maximum, with several rollback
versions per ID, and retain the result as a release budget.

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
revision `f3ac48c`; the implementation HEAD at reassessment is `0d4eb80`. Its
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
| Installed-widget isolation | Strong execution containment and digest-specific unsigned authority; current HEAD adds bounded catalog metadata, paired manifest policy, exact GBSS path/hash inventory, and digest-bound styles | Session launch lease for executable/dependency/assets; changing/insertion-path tests and clean packaged abuse/run evidence; persisted acquisition receipt and capability delta; signed publisher/update/revocation model |
| Installed catalog scale | Each version is entry/byte bounded, but all IDs/versions are eagerly hashed and current HEAD retains every version's GBSS inventory | Aggregate ID/version/file/byte/time limits, active-only inventory ownership, cleanup UX, and maximum-catalog cold/reload memory measurements |
| GBSS author diagnostics | Closed typed statuses remove false `missing_import` results, contain provider faults, and route CLI validation through the bounded reader | Add real file/import coverage for all statuses and surface installed integrity failures distinctly |
| SDK lifecycle/coordination | Media Sessions proves substantial lock/task reduction; YT Music and Spotify have adopted only selected operation/resource families | One advanced reference architecture, a second repeatable migration, and packaged churn evidence |
| Responsive/controller UI | Explicit focus identity and transition-owned reconciliation are implemented and focused tests pass | Scheduling-seam proof, real controller, and viewport matrix |
| YT Music | Active Latest transport refresh rejects stale success/failure, but one class still owns connection, loops, optimistic reconciliation, and rendering | Model/controller/view extraction plus real companion, packaged lifecycle/controller, and visual evidence |
| Spotify | Paging/resource adoption is successful, but command/auth/refresh/polling/state/view ownership remains concentrated | Credential-free full-state visuals, live auth/playback gates, and structural migration by responsibility |
| CLI author workflow | Data inspection is non-executable; source scaffolding now fails honestly without an SDK and builds externally with explicit `--sdk-project`, but has no cloneable dependency or generated snapshot exporter | Versioned public SDK/template release, packaged clean-directory scaffold/build/README proof, isolated scenario execution, native preview, provenance/signing, and automated CI |
| Performance | Per-worker Jobs plus current-HEAD aggregate admission and runtime-owned process leases; one dirty single-Settings-worker baseline sits at the Hidden CPU diagnostic edge | Direct lease fault-injection proof, ownership/remediation UI, repeated 1/8/many-widget churn, clean GPU/ETW noise-qualified regression gate |
| Visual evidence | Provenance-aware offscreen widget-body capture exists, but its dirty old Spotify 0.1.6 setup matrix neither covers current advanced states nor judges layout/visual correctness | Clean current package/state/profile matrix, semantic layout assertions, reviewed tolerant baselines, and physical full-shell/controller/DPI smoke |
| Native host ownership | Proven low-level input, focus, lifecycle, bridge, and renderer helpers, but `OverlayApp` still owns their mutable orchestration in about 3,753 lines | Extract/test one `WidgetSessionCoordinator`; remove duplicate descriptor/snapshot/lifecycle/retry state from `OverlayApp`; typed persistent session failures |
| Documentation | Extensive and now internally current, but copyable examples are not executable evidence | Compile-test canonical snippets and reduce ledger/status duplication |

## Recommended next three actions

1. **Bind package authority to every package byte actually consumed.** Add a
   supervisor-owned launch lease or protected generation that enforces the
   verified inventory, and prove neither replacement nor
   late file insertion between verification and lazy assembly/dependency load
   can execute under the old digest.
2. **Extract the native widget-session owner and finish failure UX.** Move
   bridge/catalog/snapshot/lifecycle/retry ownership into one tested
   `WidgetSessionCoordinator`, extend residency fault-path accounting, and
   surface capacity/startup/protocol failures as persistent typed per-widget
   states with retry and resource-management actions.
3. **Ship a truthful external widget scaffold.** Publish a versioned supported
   SDK/template set, generate a real snapshot fixture/exporter, and prove the
   full build/run/test/package path from an unrelated clean directory.

The next review should first reassess these three items, then rotate into test-
architecture credibility and public-documentation/API drift. Revisit visual,
performance, or advanced-widget composition when a new fixture, retained
baseline, or widget migration lands.
