# Engineering Quality Review

Status: living independent quality audit; active findings require disposition<br>
Date: 2026-08-09<br>
Last reassessed: 2026-08-09 against implementation commits `6c5f932` and `7d33ce1` plus the current documentation worktree, after unified action admission and generation-owned native failure handling, bounded bridge dispatch, exact-directory-boundary evidence, GitHub package lifecycle, digest-bound GBSS and typed source diagnostics, aggregate catalog scaling, the verified-package launch handoff and directory-shape admission, native host widget-session/failure ownership, the bounded verification gate, advanced-widget SDK adoption, hidden Guide-compatibility polling, retained visual/performance evidence, and documentation drift were audited<br>
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
history. Control-plane recovery and maximum-scale time/memory evidence remain
incomplete, so EQ-016 is only partially implemented.

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

The newest runtime follow-up usefully rejects content roots that overlap the
trusted generic-worker directory and preserves caller cancellation during
content acquisition. The security boundary still operates across separate
pathname checks, directory/file opens, final inventory, and pathname ACL writes
without a recorded object identity or handle-relative traversal. Concurrent
directory replacement is therefore unproven even though ordinary file
replacement and late insertion are covered.

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

Commits `6c5f932` and `7d33ce1` resolve the action-ingress ownership mismatch.
Direct, legacy quick, and controller-resolved actions now enter one bounded
active-lifetime FIFO and acknowledge typed admission instead of provider
completion. Capacity proof waits on a deterministic execution barrier. Public
docs define the legacy catalog QuickAction route as deliberately non-authorizing,
retain protocol-v1 empty acknowledgements and failure names, and reserve exact
capability gesture authority for snapshot-correlated controller input. YT Music
and Spotify removed redundant ordinary-action coordination. Late failures now
enter a bounded native queue with runtime-generation identity; the shell rejects
stale/malformed payloads and never renders or logs exception text. EQ-021 is
resolved with focused Release evidence; the clean aggregate gate remains a
separate verification task.

The bridge-concurrency code also needs one ownership pass before more request
types or an asynchronous native client are added. `WidgetBridgeServer.RunAsync`
now owns capacity, active-ID uniqueness, per-widget receive-order tails, task
tracking, fatal-error propagation, Stop, and drain through six interacting
mutable mechanisms, while `ClientRegistration.OperationGate` separately
serializes widget work. EQ-022 recommends a narrow internal request dispatcher
with deterministic tests; this is not a request for a generic framework or a
line-count refactor.

The accessibility rotation finds a more fundamental product gap. The SDK and
protocol validate accessible names and values, and the native host applies
visual accessibility policy, but the custom Direct2D window exposes no Windows
UI Automation or MSAA provider. There is no `WM_GETOBJECT` handling, provider
interface, or focus/property/structure event publication. Assistive technology
therefore cannot discover, navigate, invoke, or read the widget tree. EQ-023
separates visual accessibility from end-to-end platform accessibility and treats
the missing host-owned provider as a P1 release boundary.

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
tests the template does not generate. The generated source-to-package path is
also internally incomplete: its manifest requires
`payload/<WidgetName>.dll`, ordinary `dotnet build` writes beneath `bin`,
`gbar validate .` checks manifest/style semantics but not entrypoint existence,
and neither the template nor its README stages the payload before `gbar pack`.
The generator itself is not yet transactional or version-governed:
`template.json` is used only as an existence sentinel, its `templateVersion` is
never parsed, and every recursively discovered file is read as text and written
directly into the final target after that directory is created. Mid-copy failure,
unexpected files, binary starter assets, or a changed template layout have no
bounded schema, compatibility refusal, staging transaction, or rollback test.
The roughly 200-declaration public SDK surface also has no package metadata or
API-compatibility baseline yet.

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

The accessibility rotation adds EQ-023. Searches across native source and tests
find no `WM_GETOBJECT`, `UiaReturnRawElementProvider`, UIA provider interfaces,
MSAA bridge, or accessibility-event publication. `accessibilityLabel` is parsed
and validated, but the only direct use in `OverlayApp` is as fallback text for
the visible controller shortcut prompt. Renderer “accessibility” tests exercise
text scale, contrast, reduced transparency/motion, focus geometry, and semantic
field retention—not an operating-system accessibility tree. No product document
acknowledges this distinction despite repeatedly saying the host owns or provides
accessibility.

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

The external-author workflow rotation found no implementation change, but made
EQ-015 more concrete. The generated README's first two commands can succeed
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

### EQ-015 — P1 — The generated widget lacks a published standalone SDK/test contract

**Status: Partially implemented in current HEAD. Local-project SDK resolution
is truthful and build-tested; published SDK, generated snapshot/test contracts,
and the generated source-to-package path remain open.**

**Evidence.** `NewCommand` now resolves the source checkout or requires an
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

The package path is independently incomplete. The generated manifest declares
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

**Why it matters.** The misleading successful-but-unbuildable scaffold is now
removed. Standalone GitHub repositories—the intended sharing unit—still cannot
consume the SDK through a supported published dependency, and developers must
bring a platform checkout, invent a snapshot-export path, and discover a second
undocumented build/staging procedure before they can create a package. The
apparently successful validation is particularly misleading because the first
complete package validation occurs only after the author reaches `gbar pack`.
That remains short of the promised 15-minute community starter experience.

**Underlying problem.** The generator now treats local SDK resolution as a
validated dependency, but the CLI, template, SDK/runtime package, generated
tests, API-compatibility baseline, and copyable commands are not yet shipped as
one versioned release set. Development and distribution also construct package
generations through different author-facing workflows: `gbar dev` owns a useful
source build/stage implementation that `gbar pack` cannot consume. Generated
documentation remains outside executable documentation checks. The template is
also treated as an unversioned directory convention rather than one validated
input artifact owned by the same CLI release.

**Recommended direction.** Treat the CLI, template, SDK/runtime packages, and
compatibility range as one release set. The production endpoint is a supported,
immutable NuGet SDK/runtime release plus a template that pins a compatible
version and can be restored from a clean machine without the platform source.
The current explicit local-project override is appropriate for contributors
until that artifact exists; do not reintroduce a project known not to build.
Define the intended author-facing API before the first package: enable package
validation against a checked-in baseline, generate XML documentation and symbol/
source metadata, and classify intentional breaks through the same host-API/
template compatibility policy rather than silently growing a 200-type surface.

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

Give source widgets one explicit release operation that owns the same bounded
build-to-generation contract as `gbar dev`, then passes that exact immutable
generation to the existing deterministic packer. This could be `gbar package
<source>` or a carefully named `gbar pack --build`; it should not silently make
the current directory packer execute arbitrary projects. Alternatively,
generate an MSBuild publish target and explicit staging directory, but keep one
canonical command in the README and CI. Make source validation distinguish
manifest/style validation from package completeness, and ensure the generated
workflow never reports a release-ready result while its declared entrypoint is
absent.

**Tradeoff.** Failing outside the checkout temporarily exposes an unfinished
product instead of appearing convenient, but it prevents hours of misleading
restore troubleshooting. Publishing packages creates versioning, symbol/source,
provenance, and support obligations; vendoring SDK binaries into each scaffold
avoids a feed but produces opaque duplication and unsafe upgrade mechanics.
An explicit local project override is useful for platform contributors, but it
must not become the documented community distribution model.
Reusing the dev generation builder reduces drift, but release packaging must
exclude dev readiness files/catalog state and must not launch the overlay;
duplicating build/staging logic would make development and release artifacts
diverge again. A closed template manifest is slightly more maintenance than
directory enumeration, but it makes binary assets, compatibility, provenance,
and review diffs explicit. Requiring a nonexistent destination simplifies an
atomic rename; supporting an already-created empty directory would require a
more complex recoverable publication contract with little author value.

**Resolution evidence.** The implementation agent reports Release
`GbarCli.Tests` at 49/49, including a failure-before-write case and an unrelated-
directory explicit-SDK Release build. This review inspected the new case but
did not execute it or inspect retained output. The source-to-package mismatch
above is derived from the template, validator, dev-generation builder, packer,
and test code; this cycle did not execute the generated command sequence. For
full closure, run a release
test from a temporary directory with no
repository ancestor and no `GBAR_TEMPLATE_ROOT`: invoke the packaged CLI,
scaffold, restore/build with only declared prerequisites, validate, run the
generated deterministic tests/replay, package, and inspect the result. Execute
or mechanically verify every command in the generated README, including the
data-only snapshot handoff. Assert the generated release package contains the
declared entrypoint and runtime dependencies, can be installed disabled into an
empty catalog, and launches through the generic worker without reading the
source tree. Add a negative test proving an unavailable SDK
fails during scaffolding with no partial directory rather than later in
`dotnet restore`. Verify the emitted package/template versions match the host
compatibility contract, and retain an external sample repository or immutable
CI artifact as the public proof. Pack the SDK and protocol dependencies, compare
their public surface to the approved baseline, and prove an intentional breaking
change requires an explicit compatibility/version update while an accidental
one fails the gate. Inject unsupported template versions, an unreadable source,
a destination write failure, unexpected and binary files, oversized/count-
limited input, and a reparse directory; every refusal must leave the requested
target absent and clean only its own staging directory. Prove the published CLI
accepts exactly its packaged template manifest and preserves declared binary
bytes without replacement.

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
  including current-attempt guards for success and failure. Commit `6c5f932`
  now relies on the shared queue for ordinary action serialization
  and narrows its former action semaphore to connection work that may also start
  during activation. The same class still owns three activation tasks, two
  semaphores, a state lock, connection
  and polling policy, progress projection, an optimistic-command list and its
  confirmation/rollback algorithm, action routing, and the complete view.
- Spotify has successfully moved two offset collections into
  `WidgetPagedResource<TItem>` and one page family into an Active Latest lane.
  The committed action-admission migration also removes its command task
  registry and action semaphore by awaiting ordinary commands directly on the
  shared runtime queue. It still has an authorization task registry, refresh
  semaphore, active generation, polling/progress loops, several lock domains,
  state/cache fields, action routing, and all view composition in the same
  roughly 1,914-line class. Its existing widget tests call `OnActionAsync`
  directly, so production worker/bridge adoption is not yet demonstrated.
  Audio Mixer and Network Controls have not adopted the new
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

**Status: Open against implementation HEAD `6c5f932`; the file inventory and
managed request dispatcher are bounded, and clean retained focused cases prove
cooperative managed head-of-line behavior. Production-client adoption,
security-authority application, shutdown drain,
native pipe I/O, and one user-visible start deadline remain open.**

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

### EQ-021 — P1 — Unified action admission is platform-owned end to end

**Status: Resolved in commits `6c5f932`, `7d33ce1`, and `7d92dcd`.
Admission, lifecycle, compatibility, failure transport, native presentation,
and advanced-widget adoption have one explicit owner and focused Release
evidence. A clean aggregate gate remains separate release evidence.**

**Evidence.** `WidgetControllerQueue.cs` now owns a shared 16-item
`ActionQueueState`. Internal `AdmitAction` accepts both direct and resolved
controller actions into one serial active-lifetime FIFO, coalesces only a
contiguous tail of changes to the same slider, reports `Enqueued`, `Replaced`,
`RejectedCapacity`, or `RejectedInactive`, cancels/drains on lifecycle exit, and
publishes later `ActionFailed` events. `WidgetWorkerServer` acknowledges direct
protocol `Action` after admission rather than after `OnActionAsync` completes;
`WidgetProcessClient.AdmitActionAsync` exposes that distinction while
`SendActionAsync` remains a compatibility admission wrapper.

`WidgetBridgeServer` routes both bridge `Action` and the separate catalog
`QuickAction` command through `AdmitResidentActionAsync`, returns explicit
`action_inactive`/`action_saturated` failures, and emits asynchronous failure
events while preserving protocol-v1 reason `controllerActionFailed`. Updated
bridge cases assert admission, await invalidation
before reading completed state, observe a later crash event, and prove restart
can drain an admitted never-completing action. Runtime cases cover mixed direct
and controller order, bounded admission, late failure, and prompt acknowledgement
of a hung action. `DirectActionAdmissionIsBounded` now waits for a deterministic
invalidation emitted as the blocking action starts, proves slider-tail
replacement, then fills the exact remaining capacity. A compatibility case maps
an old empty acknowledgement to `Enqueued` and rejects an invalid `Completed`
value. Public `controller-input.md` and `declarative-ui.md` describe admission
versus completion, queue capacity, cancellation/drain, late failure, and the
legacy catalog QuickAction as explicitly non-authorizing. Focused Release
execution passed Widget Runtime 48/48, Widget Bridge 46/46, Widget SDK 84/84,
Spotify 31/31, YT Music 48/48, First-Party Conformance 6/6, and the 49-file
documentation contract.

Commits `7d33ce1` and `7d92dcd` complete the native and diagnostic boundary.
`WidgetBridgeServer` adds the exact runtime generation to action-failure events.
`WidgetBridgeClient` strictly distinguishes the legacy action-failure reason,
requires the closed payload and `canRestart = false`, rejects control-bearing or
over-512-character messages, and stores only validated widget/generation/action/
source identifiers in a bounded 16-item FIFO. `OverlayApp` discards mismatched
runtime generations and binds generic “action failed; try again” copy to the
affected widget in both dashboard and open-widget footers. It never renders or
logs the untrusted message. Native source tests parse a valid
event, reject control characters and false restartability, and prove oldest-
entry eviction; the bridge test asserts generation and legacy reason, and the
Release native parser target passes while the Release OverlayHost target builds.
`WidgetWorkerServer` now publishes only the stable generic `Action failed.`
message across process boundaries. Domain-aware widgets continue mapping
provider failures to safe state before they escape `OnActionAsync`.

Both advanced widgets complete the intended migration in `6c5f932`. YT
Music removes its broad action semaphore from ordinary
refresh/transport commands and retains a narrowly named `_connectionGate` only
because activation auto-connect can overlap explicit connect/pair. Spotify awaits
ordinary commands directly and removes `_actionGate`,
`_backgroundOperationGate`, `_commandOperationTask`, `StartCommandOperation`,
and their lifecycle drain. First-Party Conformance runs both community packages
through the generic isolated worker path; the credential-free Spotify route
proves setup/navigation admission, while provider-command behavior remains
covered by its typed-fake suite. Live account evidence remains a separate gate.

**Why it matters.** The original transport-dependent worker-termination hazard
is structurally removed in `6c5f932`, and `7d33ce1`/`7d92dcd` give users safe
affected-widget feedback when accepted work fails. Ordinary provider failures
are no longer described as worker crashes or silently overwritten by an
unrelated widget.

**Underlying problem.** The former implementation conflated admission,
execution, completion, domain error presentation, runtime diagnostics, and
worker crash/restart. These are now separate typed contracts with explicit
lifetime and compatibility ownership.

**Disposition.** Keep the one queue and compatibility rules stable. Explicitly
widget-lifetime work such as browser authorization remains separate; ordinary
actions must not outlive deactivation. Future ingress types must reuse typed
admission and must declare whether they can carry exact gesture authority.

**Tradeoff.** Unifying ingress changes when direct callers observe completion
and requires a bounded failure channel rather than synchronous exceptions.
Queueing pagination may require Latest/coalesced policy rather than strict FIFO.
Those policies should be explicit action metadata or SDK-owned adapters, not
separate widget task registries.

**Resolution evidence.** Runtime 48/48 proves prompt slow/hung admission,
mixed-ingress FIFO order, deterministic saturation, slider replacement,
deactivation drain, late generic failure, no restart, exact queued gesture
authority, and protocol-v1 empty-ack compatibility. Bridge 46/46 proves typed
Action/QuickAction responses, per-widget ordering, generation-owned late
failure, process-failure separation, and restart/drain. Native parser tests
prove closed payload validation and bounded eviction; the Release OverlayHost
build consumes failures only for the current runtime generation and presents
safe affected-widget feedback in either surface. Widget SDK 84/84, Spotify
31/31, YT Music 48/48, First-Party Conformance 6/6, and Documentation 1/1 pass.
The clean aggregate retained bundle is the remaining evidence step, not an open
action-ownership defect.

### EQ-023 — P1 — The custom renderer has no Windows accessibility provider

**Status: Open in implementation HEAD `7d92dcd`; semantic fields and visual
accessibility policy exist, but assistive technology has no host accessibility
tree or interaction surface.**

**Evidence.** The public SDK and `ViewSnapshotValidator` require accessible
names for icon-only actions, images, loading indicators, action surfaces, and
sliders, and require an accessible slider value. `WidgetBridgeClient` preserves
`accessibilityLabel` and `accessibilityValue`. The renderer then owns final
responsive visibility, clipping, scroll offsets, focus rectangles, navigation
geometry, high contrast, text scale, reduced transparency, and reduced motion.

That semantic pipeline stops before Windows accessibility. Repository-wide
native searches find no `WM_GETOBJECT`, `UiaReturnRawElementProvider`,
`IRawElementProvider*`, `IInvokeProvider`, `IRangeValueProvider`, MSAA
`IAccessible`/`LresultFromObject`, or `NotifyWinEvent`. `OverlayApp::WindowProc`
falls through to `DefWindowProcW` for unhandled messages. The only direct use of
a node's accessible label in `main.cpp` is `CollectShortcutPrompts`, which uses
it as visible controller-hint fallback text.

`RenderResult` retains interactive hit/focus/navigation rectangles, but exact
geometry for all nodes is compiled only under
`GBA_DECLARATIVE_RENDERER_TESTING`. Native tests described as accessibility
coverage prove visual policy and internal geometry; none acts as a UI Automation
client, inspects control types/patterns, observes focus/property/structure
events, or invokes an element through an assistive-technology interface.
Keyboard arrow/Enter/Escape support exists through `WM_KEYDOWN`, but keyboard
input is not a screen-reader tree and does not expose names, roles, values, or
state.

The documentation currently blurs those levels. `plugin-platform.md` promises
consistent accessibility, `architecture-plan.md` says the host owns
accessibility, and authoring docs describe labels as accessibility exposure.
The only screen-reader reference is PS5 research; no status or known-issue text
states that the Windows host lacks UIA/MSAA support.

**Why it matters.** Narrator and other Windows assistive technologies cannot
discover the dashboard, focused widget control, selected/disabled/busy state,
slider value, progress, error text, or route changes. Users who cannot rely on
the custom pixels receive no equivalent product. Authors are required to supply
semantic data that the shipping host does not deliver, so the framework's
accessibility promise is currently misleading.

**Underlying problem.** The project treats accessibility as validated strings
plus visual adaptation. A custom-drawn HWND needs a separate, host-owned semantic
surface whose lifetime follows the presented shell/widget generation and whose
bounds come from final clipped presentation geometry. That ownership boundary
was never implemented.

**Recommended direction.** Add one native `OverlayAccessibilityProvider` owned
by the host window. Handle `WM_GETOBJECT` with
`UiaReturnRawElementProvider`; expose an immutable fragment tree for the shell,
tray, footer/status, active route, and responsive-visible widget nodes. Build it
from the exact presented snapshot generation and final renderer geometry—never
by re-running layout or querying a worker from a COM callback. Use stable
runtime IDs derived from host surface plus widget instance/node ID, convert
clipped DIPs to screen pixels, mark hidden/offscreen nodes correctly, and retire
stale providers when the surface or widget generation changes.

Map closed node semantics to closed UIA patterns: Button/ActionSurface to
Invoke, Slider to RangeValue, read-only Progress to RangeValue, selected choices
to SelectionItem or Toggle only where the SDK state actually expresses that
contract, and text/image/icon/loading nodes to appropriate read-only control
types and names. Keep focusable and enabled separate because the platform
deliberately lets disabled/busy nodes remain controller-focusable. Provider
actions must post a generation-checked command to the UI thread rather than call
the bridge synchronously from UIA. Define explicitly whether a UIA Invoke or
RangeValue change is a trusted user gesture for capability authority.

Do not expose arbitrary automation-property bags to widgets. First implement
the provider using current closed node kinds and fields, then add only proven
typed SDK semantics that are missing—likely heading level, description/help
text, and live-region/notification intent. CSS/GBSS classes such as
`page-heading` must not become hidden semantic transport.

**Tradeoff.** UIA fragment providers, COM lifetime, event coalescing, and stale
generation handling are substantial native work. A hidden HWND child-control
tree could reuse built-in accessibility but would duplicate layout/state and
undermine the custom renderer. A single semantic provider over the existing
tree is the cleaner architecture, provided it never blocks on widget I/O and
does not retain stale snapshot objects indefinitely.

**Resolution evidence.** Add pure projection tests for responsive exclusion,
clipping/offscreen state, stable runtime IDs, names/values, selected/disabled/
busy mappings, route replacement, and stale-generation rejection. Add a native
UIA-client integration test against the real HWND that discovers dashboard and
widget elements, verifies control types/patterns/bounding rectangles and one
focus event, invokes a button, changes a slider, observes property/structure
events after a snapshot/route change, and proves hidden content disappears.
Exercise long/sanitized labels and a worker crash without blocking the provider.
Finally retain a manual Narrator smoke for dashboard, a multipage Settings
route, YT Music failure/setup state, and Spotify playback/slider state. Only
then describe the host as providing end-to-end accessibility.

### EQ-022 — P2 — Bridge scheduling policy is embedded in the transport session

**Status: Open in implementation HEAD `6c5f932`; the policy is bounded and
documented, but its ownership and deterministic verification surface are not yet
cohesive.**

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

### EQ-016 — P2 — Aggregate catalog bounds lack a recoverable control-plane contract

**Status: Partially implemented in commits `b2d6f95` and `1c1f8bb`.
Cardinality/byte budgets, prospective install rejection, and fine-grained
cancellation/deadline checkpoints are present; bridge lifetime is active-only,
but cleanup UX and maximum-scale transient inventory evidence remain open.**

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

One new custom test case covers invalid option relationships, direct ID N+1,
prospective ID/per-ID-version/total-version/entry/byte refusal, an unexpected
root file, and a deterministic elapsed-time failure. It also asserts selected
install rejections do not create the incoming package directory. The
implementation agent reports the expanded Catalog suite at 29/29; this review
did not rerun it or inspect a retained result. Its exact tree case
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

Recovery is the larger product gap. `SettingsWidget.ReloadInstalledWidgetsAsync`
catches the new codes, clears its projection, and shows only “Installed widget
catalog unavailable (<code>)”. `WidgetCatalog.UninstallAsync` itself begins
with full `DiscoverAsync`, and `gbar uninstall` has no independent repair path.
An existing catalog that exceeds new defaults after upgrade/configuration
change, or gains an unexpected entry outside the normal installer, therefore
blocks both the Settings version list and the supported removal command. The
only available recovery is manual filesystem surgery—the outcome this finding
was intended to avoid.

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

Add a separately bounded catalog-health/repair projection that can identify
safe package ID/version directory candidates and their coarse counts without
publishing or trusting their manifests. Settings and CLI should use it to name
the breached limit and remove a specifically selected disabled package/version
under the existing operation lock and path/reparse safeguards, even when full
discovery fails. Do not add a broad `--force` recursive delete or treat
unverified manifest identity as deletion authority.

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
remove a chosen safe package/version without manual deletion, after which full
discovery recovers. Prove the bridge does not retain disabled inactive GBSS
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
| Installed-widget isolation | Strong execution containment and digest-specific unsigned authority; commit `d2e49a9` adds a complete file inventory, per-start revalidation, pinned file handles, exact non-inheriting AppContainer grants, trusted-runtime overlap refusal, production-token late-file and stale-generation-root denial, and positive execution paths for five installed packages; `6fc9e01` rejects unlaunchable directory shapes before extraction; clean selected evidence is retained | Add installed-worker managed/native/asset abuse and directory replacement cases, bind ACL-applied objects to lease-authenticated identities, and prove the catalog ACL policy excludes alternate group grants and handles partial grant failure; then add acquisition receipts, capability deltas, signing, update, and revocation |
| Installed catalog scale | Commits `b2d6f95` and `1c1f8bb` add aggregate quotas, prospective refusal, and per-entry/per-read checkpoints; current discovery temporarily materializes a full file inventory for every accepted version before only active enabled versions are retained by bridge closures | Outer watchdog for kernel-blocked I/O, bounded Settings/CLI repair path, and maximum-catalog cold/reload time plus peak/transient memory measurements for full inventories |
| GBSS author diagnostics | Closed typed statuses remove false `missing_import` results, contain provider faults, and route CLI validation through the bounded reader | Add real file/import coverage for all statuses and surface installed integrity failures distinctly |
| SDK lifecycle/coordination | Media Sessions proves substantial lock/task reduction; YT Music and Spotify have adopted only selected operation/resource families | One advanced reference architecture, a second repeatable migration, and packaged churn evidence |
| Action dispatch | Commits `6c5f932`, `7d33ce1`, and `7d92dcd` unify every ingress behind typed bounded active-lifetime admission, preserve protocol-v1 compatibility, retain exact controller gesture authority, simplify YT Music/Spotify, and deliver generation-owned generic late-failure feedback in dashboard and open-widget surfaces; focused Release suites pass | Retain the clean aggregate bundle and add physical-controller/live-provider evidence; new ingress types must reuse this contract |
| Bridge scheduling | `d4291be` adds bounded correlated dispatch, same-widget receive-order chaining, saturation and duplicate-ID policy with clean retained 45/45 focused proof, but the concurrency kernel remains embedded in `RunAsync` and the shipping native client cannot pipeline | One narrow typed dispatcher with deterministic no-sleep invariant tests, a bounded forced drain, and one asynchronous native read owner/correlation table |
| Responsive/controller UI | Explicit focus identity and transition-owned reconciliation are implemented and focused tests pass | Scheduling-seam proof, real controller, and viewport matrix |
| Windows accessibility | The SDK carries validated names/values and the host applies visual text/contrast/motion policy, but the custom HWND exposes no UIA/MSAA provider or accessibility events | Host-owned immutable UIA fragment tree over final renderer geometry, closed Invoke/RangeValue/selection mappings, nonblocking generation-checked dispatch, automated UIA-client coverage, and Narrator smoke |
| YT Music | Active Latest transport refresh rejects stale success/failure, but one class still owns connection, loops, optimistic reconciliation, and rendering | Model/controller/view extraction plus real companion, packaged lifecycle/controller, and visual evidence |
| Spotify | Paging/resource adoption is successful, and commit `6c5f932` removes the command task registry/action semaphore through shared action admission; authorization, refresh, polling, state, routing, and view ownership remain concentrated | Credential-free full-state visuals, live auth/playback gates, and structural migration by responsibility |
| CLI author workflow | Data inspection is non-executable; source scaffolding fails honestly without an SDK and builds externally with explicit `--sdk-project`, but the template directory is not parsed/versioned/bounded or transactionally published; there is also no cloneable dependency, generated snapshot exporter, source-to-package staging operation, package metadata, or API-compatibility baseline for its roughly 200-declaration public SDK surface | Transactional manifest-driven template generation, versioned public SDK/template release with package/API validation, one bounded source-build/stage/pack path, packaged clean-directory execution of every generated README command, isolated scenario execution, native preview, provenance/signing, and automated CI |
| Performance | Per-worker Jobs plus aggregate admission and runtime-owned leases; active tickers are lifecycle-bound, `6fc9e01` aligns pack/install/runtime directory limits, clean retained selected exact-edge proof records 376.140 ms packing plus 2,528.883 ms through first validated render, and `d4291be` bounds managed dispatch to 16 with clean retained 45/45 proof for cooperative list/Stop responsiveness, FIFO/correlation, saturation, duplicate-ID refusal, and cleanup; exact ACL application and dispatcher drain remain unbounded, the native client cannot use pipelining and synchronously blocks the UI, and the one-machine sample is not a production budget; hidden Guide fallback still polls at 25 ms | Enforce one full start budget with cancellation-ignoring drain proof and cancellable correlation-safe off-UI-thread bridge I/O/responsiveness proof; adaptive Guide cadence with hardware latency/ETW evidence; repeated 1/8/many-widget churn and a clean GPU/wakeup gate |
| Visual evidence | Provenance-aware offscreen widget-body capture exists, but its dirty old Spotify 0.1.6 setup matrix neither covers current advanced states nor judges layout/visual correctness | Clean current package/state/profile matrix, semantic layout assertions, reviewed tolerant baselines, and physical full-shell/controller/DPI smoke |
| Native host ownership | Proven low-level input, focus, lifecycle, bridge, and renderer helpers, but `OverlayApp` still owns their mutable orchestration in about 3,753 lines | Extract/test one `WidgetSessionCoordinator`; remove duplicate descriptor/snapshot/lifecycle/retry state from `OverlayApp`; typed persistent session failures |
| Verification gate | Commit `4450cfa` adds the bounded 41-step gate; clean release-eligible run `20260809T141527Z-8946c731` passes 41/41 steps and 755 cases for `dc6b092`; clean seven-step run `20260809T152831Z-67b77c73` covers `6fc9e01`; clean three-step run `20260809T155221Z-ae6e5d8d` covers the exact edge in `4f903b0`; clean release-eligible three-step run `20260809T161934Z-5d0bee6a` covers Bridge 45/45, Documentation 1/1, and First-Party Conformance 6/6 for `fdcf253` | Run the complete checked-in Windows workflow for the current implementation commit and retain an immutable hosted artifact/link |
| Documentation | Extensive, but its green contract checks links/headings while 70 C# fences have no designated executable consumer; `plugin-platform.md` claims the starter scaffolds a test/replay workflow although it generates no test or snapshot exporter | Compile-test canonical snippets, make overview claims derive from generated-template end-to-end tests, bind status claims to exact result manifests, and reduce ledger/status duplication |

## Recommended next three actions

1. **Finish and prove the package launch authority.** Preserve the new
   supervisor-owned content lease, then execute a real installed worker through
   managed/native/asset lazy loads. Retain the new stale-generation-root denial,
   then prove directory replacement, late insertion, replacement, and alternate
   AppContainer-group ACEs cannot execute under the old digest; bind each ACL
   target to the object authenticated by the lease. Retain the aligned package-
   shape bound and the committed exact accepted/refused cases in a clean bundle.
   Put revalidation, ACL authority,
   process creation, and hello under one enforced start budget; keep unrelated
   bridge requests responsive, extract the bounded request scheduler behind a
   typed deterministic seam, move one-owner cancellable correlated pipe I/O off
   the native UI thread, and make grant cleanup/rollback explicit on every
   failure and teardown path.
2. **Retain action-admission release evidence.** Preserve the shared queue,
   protocol-v1 compatibility, non-authorizing legacy QuickAction rule,
   generation-owned native feedback, and generic cross-process diagnostic.
   Add a physical-controller smoke and live-provider YT Music/Spotify action
   evidence without folding connection/OAuth work into the active lifetime.
3. **Implement the host accessibility surface before more visual polish.** Add
   one nonblocking generation-owned UIA fragment provider over the shell and
   final presented widget geometry, with closed Invoke/RangeValue/selection
   mappings and precise focus/property/structure events. Prove it with a real
   HWND UIA-client suite and Narrator smoke across basic, multipage, failure,
   YT Music, and Spotify states. Keep the current verification runner and clean
   focused bundles, then run the complete hosted gate for the resulting commit.

The next review should first reassess these three items, then rotate into the
installed-package launch/session boundary or native session ownership if
implementation changes land. Revisit visual or performance proof when a new
fixture or retained baseline appears; specifically reassess EQ-019 when the
Guide fallback gains an injected policy seam or hardware trace, and EQ-023 when
the host gains `WM_GETOBJECT` or a semantic-provider prototype.
