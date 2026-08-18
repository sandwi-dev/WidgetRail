# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through DLV-262 is preserved in the
[2026-08-16 21:40 snapshot](history/delivery-plan/2026-08-16T21-40-35-07-00.md).
Earlier snapshots remain under `docs/history/delivery-plan/`. Snapshots are
evidence only; this file is the sole authority for current work.

## Current baseline

- Accepted production/test integration baseline on main is `15a26b9`. It
  integrates DLV-258 as `e5e5643`, DLV-259 as
  `45d75cf`, DLV-260 production/residue work as `74c6ca1`, and the accepted
  test-only Phase B follow-ups as `7297197` plus `1ff96ba`. DLV-264 is
  integrated as production `7cdcc31` plus focused tests `15a26b9`.
- Exact accepted production PID 17212 was built from DLV-264 implementation
  commit `75f1c96`, with SHA-256
  `502720EA82F5764CCD52DD36036D18EF8531D73044063D600568357305692737`.
  Main contains that accepted production tree. PID 17212 remains running under
  the tests/docs-only no-relaunch rule.
- DLV-264 production candidate `75f1c96` changed only
  `WidgetSessionCoordinator.{h,cpp}`. Its coherent tests-skipped Release has
  SHA-256 `502720EA82F5764CCD52DD36036D18EF8531D73044063D600568357305692737`
  and is visibly running as PID 17212. The user physically accepted repeated
  rapid Now Playing -> Settings -> Now Playing switching on 2026-08-18. The
  exact session retained visible lifecycle authority and valid checkpoints
  without publishing a hidden/suspended failure. Test follow-up `fb4fc85`
  passed 20 coordinator scenarios, OverlayState, WidgetLifecycle, and 305
  action-feedback checks. Independent review accepted the cumulative chain and
  integrated it through main `15a26b9`.
- DLV-263 acceptance evidence: zero logged error/failure/stale/timeout/rejection
  lines; exact-anchor and retained-overlap admissions; focus remained inside
  the viewport; 33 frames averaged 26.9 ms, one reached 52.0 ms, none exceeded
  100 ms; focused renderer coverage passed 4,915 checks.
- DLV-261 passes Platform Settings 18/18 after correcting only two stale
  button-scale expectations. DLV-262 passes Widget Bridge 90/90 after a
  serialized dependency rebuild and two exact stale-reference corrections.
- DLV-260 is accepted and integrated. Phase A was physically accepted on PID
  33088. Phase B replaced 214 Release-disabled catalog assertions with 215
  always-on checks, passed Catalog, Wrail 65/65, Runtime 77/77 in the one Tier-3
  run, and corrected the directly exposed renamed configuration ambiguity
  fixture to 5/5 without rerunning Tier 3. The retained Tier-3 result is 14
  passed steps and one now-dispositioned stale-fixture failure.
- DLV-248 remains deliberately deferred by the user.
- The user selected the new display identity **WidgetRail** and tagline
  **WidgetRail — a controller-first widget platform for Windows.** On
  2026-08-17 the user approved all four remaining DLV-257 naming decisions.
  The identity contract is frozen. The user subsequently approved a clean
  local-state break: WidgetRail uses only `%LOCALAPPDATA%\WidgetRail`; it does
  not read or migrate the old root or carry compatibility code. DLV-258 through
  DLV-260 are accepted and integrated.

## Avalonia disposition

The Avalonia experiment is closed and failed by user decision. Retained
Avalonia branches, worktrees, sources, and AVP history are evidence only. Do not
dispatch, integrate, relaunch, delete, or resume them without a new explicit
user decision. The native overlay is the sole production presentation path.

## Execution and review rules

- Operate exactly the standing `widgets` and `platform` production tasks. The
  rejected DLV-245 red-test task is preserved evidence and is not active.
- Implementation tasks select only Assigned/Ready work from this file, remain
  inside their lane, never edit reviewer-owned documents, and never push.
- The planner reviews actual diffs/evidence, integrates only accepted commits,
  commits reviewer-owned documents separately, never authors implementation or
  test code, and never pushes.
- Shared protocol, architecture, public identity, package, and persistence work
  is serialized to the named lead lane. No partial rebrand is launched or
  integrated as the accepted product.
- Preserve unrelated user work. Do not reset, rebase, amend, discard, rewrite,
  or perform destructive recovery.
- Verification is proportional: focused affected suites by default, the
  smallest cross-component group only when required, and Tier 3 only at the
  named DLV-260 checkpoint.
- Visible UI corrections use physical-first ordering when assigned: production
  code and Release build, user verdict, then focused tests. Tests never
  legitimize rejected behavior.
- Rebuild/repackage/relaunch after an accepted integrated milestone only when
  production/runtime artifact inputs changed. Retain the running accepted
  instance for tests-only or planner-document-only integration.
- Stop for credentials, destructive migration/reset, substantial conflict,
  undocumented APIs, external publication/account action, physical-only
  evidence, or a material architecture/security/product choice.

## Durable architecture decisions

- Taffy is the sole declarative Flex/Responsive Grid geometry owner. Native code
  retains scroll, clipping, DPI/pixel policy, focus-follow, input,
  accessibility, rendering, animation, and HWND placement.
- One tightly bounded content HWND and one tightly bounded fixed-chrome HWND are
  coordinated endpoints of one overlay session/window, graphics, GameInput,
  focus, and accessibility owner. One backdrop HWND may cover the monitor.
- Chrome placement is client-local-first. Only the chrome HWND has an absolute
  screen rectangle; guide/tray screen bounds are downstream projections.
- The tray remains small host-owned arithmetic. Do not migrate it to Taffy
  without new evidence.
- Games & Apps is bundled first party. Spotify, Game Launcher, and YT Music are
  ordinary Community applications. Core code contains no service-specific
  identity, DTO, API, or known-tree behavior.
- Complete snapshots are retained last-admitted checkpoints. Generic typed
  operations update them; semantic, resolved-resource, focus, scroll, press,
  slider, layout, UIA, composition, and placement authority remain separate.
- Sandboxed and full-trust widgets share the bounded host-admission protocol.
  Full trust grants no overlay HWND/render/input/focus authority.
- No second GameInput reader, bridge transport, semantic schema,
  lifecycle/cache owner, overlay session, graphics owner, or focus tree.
- The product is pre-release and single-user. Intentional breaking changes may
  start from fresh explicitly owned local state. Do not add old-version
  migration or compatibility code without a later explicit user decision;
  external provider data, credentials, accounts, applications, and user files
  are preserved.

## Active task map

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | Clean and idle at source-review-in-progress DLV-266 production candidate `7235849`. Reconcile accepted main `15a26b9` onto the preserved candidate, rebuild one coherent tests-skipped Release, and stop for planner launch. The previously reported pre-DLV-264 artifact hash is superseded by that required coherent rebuild. The platform queue intentionally contains one evidenced item rather than filler and requires replenishment after its diagnosis. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | DLV-264 is accepted and integrated through main `15a26b9`; exact accepted production PID 17212 remains running. Reconcile accepted main and execute assigned DLV-265 in physical-first mode. DLV-248 remains deferred. |

## Completed planner deliverable — DLV-257: select and freeze WidgetRail identity

Owner/baseline: user plus planner from accepted main `1ae21df`. This is a
decision and inventory deliverable, not implementation authority.

Required output:

- Record the exact display name, technical PascalCase namespace root, lowercase
  package/repository slug, publisher attribution, and whether the existing
  `gbar` CLI/config prefix is retained or renamed.
- Verify basic web, source-repository, Microsoft Store-name, domain, and relevant
  trademark-search availability. Availability is evidence, not legal clearance.
- Obtain the user's explicit approval of the final identity. Recommend qualified
  trademark counsel before public release; do not contact counsel, Microsoft,
  registrars, or third parties without new authority.
- Produce an exact old-to-new inventory for visible strings, SDK/NuGet identity,
  namespaces, assemblies, manifest provider types, package metadata, local-data
  roots, pipe/mutex/window/UIA identifiers, user-agent strings, templates,
  samples, active docs, store collateral, repository name, and build outputs.
- State whether any legacy identifiers are migration inputs, which are removed
  at cutover, and which historical records remain intentionally unchanged. The
  later user-approved amendment sets the active answer to no migration inputs.

Completed artifact: `docs/widgetrail-identity-migration-contract.md` contains
the complete approved mapping and preliminary availability evidence. On
2026-08-17 the user approved `WidgetRail Project`/`widgetrail`, all four new
archive/style names, the `wrail` tool/config family, and re-authentication rather
than automatic Credential Manager migration.

Acceptance: one reviewer-owned identity/migration contract committed locally,
with the chosen name approved by the user and every DLV-258/DLV-259 input
unambiguous. No production/test changes, Store reservation, external account
action, push, publication, directory move, build, or relaunch.

Stop for an unchosen name, meaningful legal ambiguity, unavailable Store or
repository identity, required third-party account/credential, competing
migration policies, or external action.

## Accepted serialized deliverable — DLV-258: cut over public managed identity

Owner/baseline: widgets lead from a fresh branch at planner main containing the
completed DLV-257 contract. This is a serialized cross-lane identity milestone:
no other lane edits SDK, package, assembly, manifest, template, style, or
managed namespace identity concurrently.

Rename the user-facing product brand and public managed identity coherently:
SDK/NuGet package ID, public namespace root, assembly/root namespaces, manifest
provider types, managed package metadata, CLI discovery, package/style formats,
templates, samples, first-party and Community package sources, active public
documentation, and directly affected deterministic tests. Use only the
DLV-257 mapping. Do not retain dual public SDK namespaces, compatibility
facades, type forwarders, duplicated manifests, or old/new publication paths.
Do not touch immutable history, closed Avalonia sources, native IPC/HWND
identifiers, or local persisted state in this milestone.

Acceptance: all managed projects and public examples compile from the new
identity; manifests resolve only new provider types; a newly packed SDK exposes
only the new package/namespace identity; current packages publish without old
managed assemblies; and a repository scan reports no unexplained old active
managed identity. Run only focused managed build/package/contract suites; no
Tier 3 and no launch. Commit one coherent DLV-258 milestone and leave it
unintegrated/unlaunched pending DLV-259.

Accepted result: commit `5b924f4` changes 544 files with 6,291 insertions and
6,247 deletions. Focused Release evidence passes the managed/conformance build
with zero warnings/errors, WidgetSdk 89/89, compatibility 12/12, Wrail CLI and
external package flow 65/65, WRSS 23/23, catalog 35/35, Bridge 90/90,
first-party/Community conformance 6/6, 75 active Markdown files, and the
verification-runner self-test. Review found zero approved old project-owned
package IDs or old public archive/style formats in active managed scope. The
remaining old names are deferred native/AppContainer identities, persisted
paths, exact retired-output cleanup sentinels, opaque test IDs, and
immutable/closed evidence. No native suite, Tier 3, launch, integration,
publication, push, credential access, or user-state migration occurred.

Stop for a public compatibility requirement, external package publication,
third-party credential/account action, generated-output ambiguity, substantial
conflict, user-data change, native identity change, or a second SDK/package
owner. Never push.

## Accepted serialized deliverable — DLV-259: cut over native identity and fresh local state

Owner/baseline: platform lead after accepted DLV-258 is supplied on its exact
serialized baseline. Own native display/accessibility identity, singleton
activation identity, local product-owned state root, runtime path consumers,
and directly affected tests. No other lane edits those identities concurrently.

Apply the DLV-257 mapping to native titles, accessibility product names, app
manifest assembly identity, HWND/text-entry/pinned-surface classes, singleton
mutex and activation pipe, user agent, startup/log/runtime paths, and the new
product-owned local-data root. Also update native consumers of the now-approved
managed package IDs, `.wrwidget`/WRSS formats, catalog fields, styles, and CLI
names so the combined DLV-258/DLV-259 Release has no split identity. Implement
a fresh `%LOCALAPPDATA%\WidgetRail` store. New code reads and writes only the
new root and must not probe, read, copy, move, merge, reinterpret, stage, delete,
or otherwise touch `%LOCALAPPDATA%\GameBarAlternative`. Do not add old package
or persisted-format importers, legacy root fallbacks, old singleton/pipe
activation compatibility, version bridges, or other support for pre-release
versions. Preserve external provider data, credentials, accounts, installed
applications, user files, and the untouched old product root. Settings,
consents, packages, and credentials are configured again under WidgetRail.

Acceptance: focused native cases prove one new singleton/activation owner,
expected UIA identity, new paths, bounded failure behavior, clean shutdown, and
a fresh new-root startup. Focused path cases prove the new build never accesses
the old root and has no legacy fallback/import path. Build the coherent combined
DLV-258/DLV-259 Release but do not launch or integrate the partial chain before
DLV-260 review. Tier 2 is limited to the smallest managed/native/package
boundary; no Tier 3 yet.

Stop for any proposed legacy compatibility path, any access to or mutation of
the old root, credential/external-provider handling, simultaneous old/new
runtime ownership, undocumented Windows APIs, substantial conflict, or
data-loss risk. Never push.

Accepted result: implementation commit `188cc64` changes 52 files with 254
insertions and 194 deletions. Independent review confirms a mechanical native
cutover for UIA/window/singleton/pipe/AppContainer identity, runtime/local paths,
package/style consumers, persisted headers, and current scripts. Active
production source contains no old-root reader or compatibility bridge. The
coherent tests-skipped Release built successfully with `OverlayHost.exe`
SHA-256 `16D5BE7CC2D831BA72D701A8C5C63086C4EFCB790FF6ABAFAC69CAE6056A46F8`.
Focused native evidence passed for process ownership, package import, pinned
placement, surface coordination, accessibility, renderer, and settings.

Two disclosed verification routes are inconclusive rather than production
failures. Widget Runtime stopped after six cases at its existing seventh-case
hang. `WidgetBridgeCatalogTests.exe` appeared to hang but actually opened a
native null-read dialog: the Release build defines `NDEBUG`, while the fixture
uses disabled `assert(...)` guards around values later dereferenced outside the
guards. This test-harness defect predates DLV-259; the DLV-259 test diff only
adapts an already-changed result wrapper and `.wrwidget` spelling. Repair it
after the user's visible DLV-260 verdict, not before.

## Accepted integrated deliverable — DLV-260: audit and accept complete rebrand

Owner/baseline: serialized lead plus planner review after DLV-258 and DLV-259.
Complete active tests, scripts, examples, publication metadata, notices, and
nonhistorical documentation required by the DLV-257 mapping. Remove stale
old-name artifacts from validated build/package output before republishing the
coherent Release. Do not rewrite Git history, immutable reviewer snapshots,
closed Avalonia evidence, third-party notices that accurately quote historical
names, or external repositories/store listings without explicit user authority.
Renaming the local checkout directory and saved Codex project is optional and
performed last by the planner only if the user requests it.

Execution and acceptance are explicitly physical-first:

1. Phase A classifies every active old-name residue, corrects only active
   production/public identity and validated generated outputs, proves the SDK
   and installed packages expose only WidgetRail, and builds one coherent
   tests-skipped renamed Release. Do not run or repair tests in this phase.
2. After independent source/output review, launch that Release for the user's
   visible verdict. The user will configure fresh settings/consent/packages and
   re-authenticate as needed. The old local-data root remains untouched.
3. Only after the user accepts the visible Release, replace disabled/unsafe
   `assert(...)` use in `WidgetBridgeCatalogTests` with always-on checks without
   weakening coverage, disposition the existing Widget Runtime seventh-case
   hang, and run the smallest focused package/runtime routes plus the one named
   Tier-3 integration checkpoint.
4. Integrate the complete accepted DLV-258/DLV-259/DLV-260 chain after that
   evidence. If the only final delta is tests/docs, retain the already accepted
   running Release rather than rebuilding or relaunching it.

Phase A candidate result: implementation commit `70af468` completed the active
production/public residue and generated-output cleanup and built one coherent
tests-skipped Release. It was initially rejected for also editing two
reviewer-owned documents; correction commit `85a021a` restores those paths
exactly without changing production. Independent cumulative review passes
`git diff --check`, finds no retired identity in active production source, and
confirms `OverlayHost.exe` SHA-256
`1E816334FD5B48ABB9449CB66FEA66CE0A93E14E8DFECEB259FE6C1753BE503D`.
The exact candidate is visibly running as PID 33088 from the isolated Phase A
Release root. Fresh `%LOCALAPPDATA%\WidgetRail` startup elected one process
owner, admitted Settings, and logged no startup error. The user physically
accepted this exact current Release on 2026-08-17. The prior candidate PID
34364 later accepted an authenticated `Show` activation while its logical and
Win32 visibility state still treated the overlay as already visible, even
though the user could not see it. The host reapplied placement but did not
produce an observable fresh foreground transition; the confirmation diagnostic
is edge-triggered, so its absence alone does not prove foreground acquisition
was skipped. Restarting the same artifact recreated the window/presentation
state, restored foreground activation, and loaded all seven tray entries. The
earlier DirectComposition fallback is not a demonstrated cause because many
successful open/close transitions followed it. Await the user's visible verdict
before any test repair, Tier 3, or integration; retain the reopening observation
and its presentation-state observability gap unless the user confirms it was
environmental. Phase B commit `f1dd235` replaced 214 Release-disabled
assertions with 215 always-on checks and corrected only stale catalog fixture
indices/appearance data. Catalog and Wrail 65/65 passed; the one Tier-3 run at
that exact commit passed Runtime 77/77 and 14 total steps, then exposed a stale
renamed configuration ambiguity fixture. Correction `473f95f` models two valid
owning dotted publishers and passes the focused configuration route 5/5; Tier 3
was proportionally not rerun. The accepted chain is integrated through main
`1ff96ba` while PID 33088 is retained under the tests/docs-only no-relaunch
rule.

Stop for unresolved old active identity, external Store/repository/domain
action, credential need, destructive cleanup outside validated generated
outputs, any old-root access, unexplained package residue, substantial conflict,
or legal/product name uncertainty. Never push or publish.

## Accepted integrated widgets deliverable — DLV-264: prevent stale hidden-snapshot failure publication

Correct the pre-existing Now Playing lifecycle race observed during a rapid
Now Playing -> Settings -> Now Playing switch. A background snapshot request
can be admitted after the widget is selected and visible, then fail as if the
widget were still hidden and suspended; that older failure can replace a valid
visible checkpoint and persist even after the newer interactive lifecycle
request succeeds.

Keep the fix inside the existing session/lifecycle authority. Do not issue a
background snapshot for the currently selected visible widget. Let a newer
visible/interactive request supersede or cancel an older background refresh.
Reject older snapshot failures using request and lifecycle authority rather
than runtime generation alone. Retain the last valid host checkpoint instead of
promoting this secondary hidden-cache miss to a persistent visible failure.
Do not mix this work into DLV-259/DLV-260, the renderer, or deferred DLV-248.

Acceptance follows physical-first ordering: implement the bounded correction
and build a coherent Release; the user rapidly switches away from and back to
Now Playing and confirms that valid content returns without the hidden-suspended
failure; only after that visible acceptance add focused lifecycle sequencing,
supersession, stale-failure rejection, and checkpoint-retention tests. Integrate
only the user-accepted production change and its accepted test follow-up.

Candidate result: merge `f6fcf6c` reconciled the widgets lane to accepted main
with a byte-identical tree, then production commit `75f1c96` changed only the
existing session coordinator. Source review confirms lifecycle-stamped snapshot
requests, bounded supersession of mismatched queued/in-flight snapshots, typed
wrong-lifecycle rejection, and retention of the last valid checkpoint. The
coherent tests-skipped Release is visibly running as PID 17212 with SHA-256
`502720EA82F5764CCD52DD36036D18EF8531D73044063D600568357305692737`.
The user physically accepted repeated rapid Now Playing -> Settings -> Now
Playing switching on 2026-08-18. Exact session traces kept the returning widget
on visible lifecycle requests, retained current/valid presentations, and
contained no persistent hidden/suspended failure. Test-only follow-up `fb4fc85`
adds deterministic lifecycle supersession, cancellation, wrong-lifecycle
rejection, and checkpoint-retention coverage. The focused group passed 20
coordinator scenarios, OverlayState, WidgetLifecycle, and 305 action-feedback
checks. Independent review accepted the cumulative commits and integrated them
as main production `7cdcc31` plus tests `15a26b9`; PID 17212 is intentionally
retained because the post-verdict delta is tests and reviewer documents only.

Stop for a protocol redesign, renderer changes, loss of authoritative provider
failure reporting, destructive state action, substantial conflict, or evidence
that the incident has a different owner. Never push.

## Assigned widgets deliverable — DLV-265: controller-first Spotify onboarding

Owner/baseline: widgets lane from accepted main `15a26b9`, containing the
complete DLV-258/DLV-259/DLV-260 rename and accepted DLV-264. Keep the work inside the
Spotify Community full-trust package and the existing public `UI.TextEntry`
contract; do not add service-specific behavior to WidgetSdk, WidgetProtocol,
WidgetBridge, PlatformBroker, OverlayHost, or Settings.

Replace the terminal-only Client ID setup route with an in-widget,
controller-first flow. The setup page must explain the Spotify developer-app
step, provide package-owned actions to open the developer dashboard and copy
the exact non-secret redirect URI, and let the user enter or replace the public
Client ID through the existing bounded host text-entry modal. The committed
value is validated by the Spotify application owner, saved atomically to the
existing package-scoped non-secret WidgetRail configuration store, and followed
by a fresh configuration check. Changing Client ID must retain the existing
fail-closed behavior that removes an OAuth refresh credential belonging to the
old client before the new identity becomes active.

The Spotify Client ID is public configuration, not a secret. Never request or
store a Spotify Client Secret. Browser authorization continues to use PKCE;
actual refresh credentials remain in the package-owned Windows Credential
Manager vault and never enter snapshots, ordinary text-entry state, logs, or
the non-secret JSON configuration document. The `wrail config` command may
remain a documented developer/diagnostic route, but the product UI must not
require a terminal, repository checkout, source directory, or copyable command
to complete ordinary setup. Do not create a new generic clipboard, secret-
entry, configuration, or credential protocol in this milestone.

Acceptance follows physical-first ordering. Build one coherent Release and let
the user complete setup from the overlay using controller plus keyboard text
entry, with no terminal command. Verify the dashboard-open and redirect-copy actions,
valid and invalid Client IDs, replacement behavior, immediate configured-state
refresh, browser PKCE connection, cancellation, and readable controller focus.
Only after the user's visible verdict add focused Spotify presentation/action,
configuration-write, old-credential invalidation, and failure/cancellation
tests. No Tier 3 is required unless the assignment changes a shared boundary.

Stop for a new shared protocol or capability, a request for Client Secret,
Credential Manager migration, third-party authentication needed for automated
evidence, provider-dashboard automation, substantial conflict, or behavior
outside the Spotify Community package. Never push.

## Assigned platform deliverable — DLV-266: reconcile invisible resident Show activation

Owner/baseline: platform lane after reconciling accepted main `15a26b9` onto
the preserved production candidate `7235849`. Keep
the correction inside the existing OverlayHost process owner, window/session,
composition/fallback, placement, and foreground-input authorities. Do not add a
second activation channel, input owner, overlay window, watchdog process, or
product restart workaround.

Correct the reproduced state divergence in which the resident process accepts
an authenticated `Show`, logical surface state and `IsWindowVisible` still say
visible, placement is reapplied, but no overlay is physically observable. Add
typed diagnostics at the existing activation decision for logical surface,
Win32 visibility, foreground ownership result, presentation mode, current
window bounds/z-order inputs, and whether a real show/recovery transition was
performed. Reconcile an already-visible `Show` through one bounded idempotent
path that can restore the accepted presentation and foreground state without
requiring process restart or disturbing unrelated foreground applications.

Do not assume the earlier DirectComposition destination-draw fallback caused
the activation failure: many successful open/close transitions occurred after
that fallback. Preserve the legacy layered-HWND fallback and ordinary
already-visible activation. Do not continuously raise/topmost-poll the window,
steal focus while hidden, force-close another process, or hide a provider/window
failure behind a successful activation result.

Acceptance follows physical-first ordering. Implement production and typed
diagnostics only, source-review the documented Win32/DirectComposition paths,
build one coherent Release, and stop for planner launch/user testing. The user
must verify Guide open/close plus second-invocation `--show` across repeated
visible/hidden and external-foreground transitions. After visible acceptance,
add focused deterministic state-decision, idempotent recovery, failure-reporting,
and no-focus-while-hidden coverage. Inspect the exact candidate log; no Tier 3
unless the implementation changes a shared protocol or process boundary.

Candidate status: the platform lane reports clean production-only commit
`7235849`, changing `main.cpp`. Its earlier tests-skipped Release SHA-256
`1A0C7F5D7EC09F1C6DC3851060F17E638C5E74F0ADE510197777D82C1ADBADFB`
predates accepted DLV-264 and is no longer eligible for launch. Source review is
in progress. Reconcile accepted main `15a26b9` onto the preserved candidate,
build one coherent cumulative tests-skipped Release, and stop for planner
launch/user testing.

Stop for inability to distinguish a product defect from external z-order state,
an undocumented Windows API, a new watchdog/process/window/input authority,
destructive process termination, substantial conflict, or evidence that the
failure owner is outside the native host. Never push.

## Ready later — DLV-248: media optimistic command revision reconciliation

Prevent an older provider publication from overwriting a newer host-projected
play/pause command while that command is pending. Use one bounded command
revision/acknowledgement policy inside the existing media widget owner, preserve
authoritative provider correction and timeout/failure rollback, and avoid
special handling in the native host or snapshot protocol. This remains
deliberately deferred by user decision and requires explicit promotion.

## Serialized order

1. DLV-257 through DLV-260 are accepted and integrated through main `1ff96ba`.
2. DLV-264 is accepted and integrated through main `15a26b9`; accepted PID
   17212 remains running because the final delta was tests/docs.
3. Reconcile DLV-266 onto main `15a26b9`, then review and physically test it.
4. Run DLV-265 from main `15a26b9` so ordinary Spotify setup no longer requires a
   repository-local terminal command.
5. DLV-248 remains outside this sequence until the user promotes it.

## Manual, external, and blocked evidence

| Item | Blocker / required evidence |
| --- | --- |
| DLV-257 identity | Complete: all four exact mapping decisions approved on 2026-08-17. |
| Microsoft Store name | Partner Center availability/reservation through the user's account; public search is insufficient. |
| Domain | Live registrar/RDAP availability and optional registration through the user's account. |
| Trademark | Similar-mark clearance for related software/services; qualified counsel recommended before public release. |
| GitHub identity | User-selected owner plus repository/organization availability and optional rename/creation. |
| DLV-264 | Complete: physically accepted on PID 17212, focused-tested, and integrated through main `15a26b9`. |
| DLV-265 | Assigned from accepted main `15a26b9`; real Spotify authorization remains a user-owned manual check. |
| DLV-266 | Reconcile candidate `7235849` onto accepted main `15a26b9`; physical user verdict required before tests. |
| DLV-248 | Deliberately deferred until explicit user promotion. |
| Avalonia | Failed/cancelled; requires a new explicit user decision. |
| YT Music catalog cleanup | Approval to remove only inactive, non-selected 0.2.0. |
| Trusted fixed-video/PiP | Changed resource budget or authorized experiment. |
| Audio default-device selection | Documented supported Windows setter plus reversible hardware/provider plan. |
| Native uninstall reconciliation | Deterministic disabled/nonresident removal evidence. |
| YouTube authenticated library | Approved minimum-scope OAuth/account; Watch Later is unsupported by the Data API. |

## Recent accepted milestones

| Milestone | Result |
| --- | --- |
| DLV-264 | Physically accepted and integrated through `15a26b9`: lifecycle-owned snapshot supersession, stale wrong-lifecycle failure rejection, retained valid checkpoints, 20 coordinator scenarios, OverlayState, WidgetLifecycle, and 305 action-feedback checks; PID 17212 retained. |
| DLV-260 | Physically accepted and integrated through `1ff96ba`: complete active WidgetRail cutover, always-on catalog checks, focused configuration 5/5, retained one-shot Tier-3 evidence with 14 passed steps and the corrected stale-fixture red; PID 33088 retained. |
| DLV-259 | Accepted and integrated as `45d75cf`: complete native WidgetRail identity and fresh-root cutover with no old-root reader or compatibility bridge. |
| DLV-258 | Accepted and integrated as `e5e5643`: complete public managed WidgetRail cutover with focused managed/package/SDK evidence green. |
| DLV-263 | Physically accepted and integrated through `110b42b`: retained-overlap viewport position, exact-anchor behavior, conservative fallback, 4,915 renderer checks, accepted PID 28612 retained. |
| DLV-262 | Test-only integrated as `88b21b1`: exact failure ledger, current dependency graph, unchanged authenticated named-pipe path, Widget Bridge 90/90. |
| DLV-261 | Test-only integrated as `c519df4`: exactly two stale button-scale expectations corrected, Platform Settings 18/18. |
| DLV-256 | Physically accepted and integrated through `b0d53b0`: bounded artwork LRU, measured full-surface transport promotion, retained renderer work, focus-scroll convergence, 4,894 checks. |
| DLV-254 | Physically accepted and integrated through `57fb469`: optional widget-switch animation defaults Off; cold and warm Off paths snap to exact destination; explicit On remains animated. |
| DLV-253 | Physically accepted and integrated through `91177ec`: Games & Apps retains resolved state, cold entry starts first row, in-session focus restores, 65/65. |

Do not mark the continuing delivery goal complete. Continue until the user
pauses/replaces it or all useful lanes are genuinely blocked. Never push.
